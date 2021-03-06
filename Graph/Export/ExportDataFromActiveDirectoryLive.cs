﻿//
// Copyright (c) Ping Castle. All rights reserved.
// https://www.pingcastle.com
//
// Licensed under the Non-Profit OSL. See LICENSE file in the project root for full license information.
//
using PingCastle.ADWS;
using PingCastle.Graph.Database;
using PingCastle.Graph.Export;
using PingCastle.Graph.Reporting;
using PingCastle.misc;
using PingCastle.RPC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PingCastle.Export
{
    public class ExportDataFromActiveDirectoryLive
    {
        string Server;
        int Port;
        NetworkCredential Credential;

		List<string> properties = new List<string> {
                        "distinguishedName",
						"displayName",
                        "name",
                        "objectSid",
						"objectClass",
                        "nTSecurityDescriptor",
                        "member",
                        "adminCount",
                        "gPLink",
                        "gPCFileSysPath",
						"lastLogonTimestamp",
                        "scriptPath",
                        "primaryGroupID",
						"sAMAccountName",
						"servicePrincipalName",
                        "sIDHistory",
						"userAccountControl",
            };

		public LiveDataStorage Storage { get; set; }
        
        public ExportDataFromActiveDirectoryLive(string server, int port, NetworkCredential credential)
        {
            Server = server;
            Port = port;
            Credential = credential;
			Storage = new LiveDataStorage();
        }

        private void DisplayAdvancement(string data)
        {
            string value = "[" + DateTime.Now.ToLongTimeString() + "] " + data;
            Console.WriteLine(value);
            Trace.WriteLine(value);
        }

		public GraphObjectReference ExportData(List<string> UsersToInvestigate)
        {
            ADDomainInfo domainInfo = null;
            IRelationFactory relationFactory = null;
			GraphObjectReference objectReference = null;
			DisplayAdvancement("Getting domain information (" + Server + ")");
            using (ADWebService adws = new ADWebService(Server, Port, Credential))
            {
                domainInfo = GetDomainInformation(adws);
				Storage.Initialize(domainInfo);
                Trace.WriteLine("Creating new relation factory");
				relationFactory = new RelationFactory(Storage, domainInfo);
				relationFactory.Initialize(adws);
                DisplayAdvancement("Exporting objects from Active Directory");
				objectReference = new GraphObjectReference(domainInfo);
				ExportReportData(adws, domainInfo, relationFactory, Storage, objectReference, UsersToInvestigate);

            }
            DisplayAdvancement("Inserting relations between nodes in the database");
            Trace.WriteLine("Inserting relations on hold");
			Storage.InsertRelationOnHold();
			Trace.WriteLine("Add trusted domains");
			AddTrustedDomains(Storage);
            Trace.WriteLine("Done");
            DisplayAdvancement("Export completed");
			DisplayAdvancement("Doing the analysis");
			return objectReference;
        }

        private ADDomainInfo GetDomainInformation(ADWebService adws)
        {
            ADDomainInfo domainInfo = null;
            
            domainInfo = adws.DomainInfo;
            if (adws.useLdap)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Performance warning: using LDAP instead of ADWS");
                Console.ResetColor();
            }
            // adding the domain sid
            string[] propertiesdomain = new string[] {"objectSid",
            };
            WorkOnReturnedObjectByADWS callback =
                (ADItem aditem) => 
                { 
                    domainInfo.DomainSid = aditem.ObjectSid;
                };

            adws.Enumerate(domainInfo.DefaultNamingContext,
                                            "(&(objectClass=domain)(distinguishedName=" + domainInfo.DefaultNamingContext + "))",
											propertiesdomain, callback);
			// adding the domain Netbios name
			string[] propertiesNetbios = new string[] { "nETBIOSName" };
			adws.Enumerate("CN=Partitions," + domainInfo.ConfigurationNamingContext,
											"(&(objectCategory=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3)(nETBIOSName=*)(nCName=" + domainInfo.DefaultNamingContext + "))",
											propertiesNetbios,
											(ADItem aditem) =>
											{
												domainInfo.NetBIOSName = aditem.NetBIOSName;
											}
											, "OneLevel");

			if (domainInfo.ForestFunctionality >= 5)
			{
				properties.Add("msDS-AllowedToActOnBehalfOfOtherIdentity");
			}

            return domainInfo;
        }

        private void ExportReportData(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, IDataStorage storage, GraphObjectReference objectReference, List<string> UsersToInvestigate)
        {
            ADItem aditem = null;
			foreach (var typology in objectReference.Objects.Keys)
			{
				var toDelete = new List<GraphSingleObject>();
				foreach (var obj in objectReference.Objects[typology])
				{
					DisplayAdvancement("Working on " + obj.Description);
					aditem = Search(adws, domainInfo, obj.Name);
					if (aditem != null)
						relationFactory.AnalyzeADObject(aditem);
					else
					{
						Trace.WriteLine("Unable to find the user: " + obj.Description);
						toDelete.Add(obj);
					}
				}
				foreach(var obj in toDelete)
				{
					objectReference.Objects[typology].Remove(obj);
				}
			}
			foreach (string user in UsersToInvestigate)
			{
				DisplayAdvancement("Working on " + user);
				aditem = Search(adws, domainInfo, user);
				if (aditem != null)
				{
					string userKey = user;
					if (aditem.ObjectSid != null)
					{
						userKey = aditem.ObjectSid.Value;
					}
					objectReference.Objects[Data.CompromiseGraphDataTypology.UserDefined].Add(new GraphSingleObject(userKey, user));
					relationFactory.AnalyzeADObject(aditem);
				}
				else
					Trace.WriteLine("Unable to find the user: " + user);
			}
            
            AnalyzeMissingObjets(adws, domainInfo, relationFactory, storage);
        }

		int AnalyzeMissingObjets(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, IDataStorage Storage)
        {
            int num = 0;
            while (true)
            {
                List<string> cns = Storage.GetCNToInvestigate();
                if (cns.Count > 0)
                {
                    num += cns.Count;
                    ExportCNData(adws, domainInfo, relationFactory, cns);
                }
                List<string> sids = Storage.GetSIDToInvestigate();
                if (sids.Count > 0)
                {
                    num += sids.Count;
                    ExportSIDData(adws, domainInfo, relationFactory, sids);
                }
				List<int> primaryGroupId = Storage.GetPrimaryGroupIDToInvestigate();
				if (primaryGroupId.Count > 0)
				{
					num += primaryGroupId.Count;
					ExportPrimaryGroupData(adws, domainInfo, relationFactory, primaryGroupId);
				}
                List<string> files = Storage.GetFilesToInvestigate();
                if (files.Count > 0)
                {
                    num += files.Count;
                    ExportFilesData(adws, domainInfo, relationFactory, files);
                }
                if (cns.Count == 0 && sids.Count == 0 && primaryGroupId.Count == 0 && files.Count == 0)
				{
					return num;
				}
            }
        }

        private void ExportCNData(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, List<string> cns)
        {
            WorkOnReturnedObjectByADWS callback =
                    (ADItem aditem) =>
                    {
                        relationFactory.AnalyzeADObject(aditem);
                    };

            foreach (string cn in cns)
            {
                adws.Enumerate(domainInfo.DefaultNamingContext,
                                                "(distinguishedName=" + ADConnection.EscapeLDAP(cn) + ")",
												properties.ToArray(), callback);
            }
        }

        private void ExportSIDData(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, List<string> sids)
        {
            WorkOnReturnedObjectByADWS callback =
                    (ADItem aditem) =>
                    {
                        relationFactory.AnalyzeADObject(aditem);
                    };

            foreach (string sid in sids)
            {
                adws.Enumerate(domainInfo.DefaultNamingContext,
												"(objectSid=" + ADConnection.EncodeSidToString(sid) + ")",
                                                properties.ToArray(), callback);
            }
        }

		private void ExportPrimaryGroupData(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, List<int> primaryGroupIDs)
        {
            WorkOnReturnedObjectByADWS callback =
                    (ADItem aditem) =>
                    {
                        relationFactory.AnalyzeADObject(aditem);
                    };

			foreach (int id in primaryGroupIDs)
            {
                adws.Enumerate(domainInfo.DefaultNamingContext,
												"(primaryGroupID=" + id + ")",
												properties.ToArray(), callback);
            }
        }


        private void ExportFilesData(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, List<string> files)
        {
            if (Credential != null)
            {
                using (WindowsIdentity identity = NativeMethods.GetWindowsIdentityForUser(Credential, domainInfo.DnsHostName))
                using (var context = identity.Impersonate())
                {
                    ExportFilesDataWithImpersonation(adws, domainInfo, relationFactory, files);
                    context.Undo();
                }
            }
            else
            {
                ExportFilesDataWithImpersonation(adws, domainInfo, relationFactory, files);
            }
        }

        private void ExportFilesDataWithImpersonation(ADWebService adws, ADDomainInfo domainInfo, IRelationFactory relationFactory, List<string> files)
        {
            // insert relation related to the files already seen.
            // add subdirectory / sub file is the permission is not inherited
            BlockingQueue<string> queue = new BlockingQueue<string>(200);
            int numberOfThread = 20;
            Thread[] threads = new Thread[numberOfThread];
            try
            {
                ThreadStart threadFunction = () =>
                {
                    for (; ; )
                    {
                        string fileName = null;
                        if (!queue.Dequeue(out fileName)) break;
                        
                        // function is safe and will never trigger an exception
                        relationFactory.AnalyzeFile(fileName);

                    }
                    Trace.WriteLine("Consumer quitting");
                };
                
                // Consumers
                for (int i = 0; i < numberOfThread; i++)
                {
                    threads[i] = new Thread(threadFunction);
                    threads[i].Start();
                }

                // do it in parallele (else time *6 !)
                foreach (string file in files)
                {
                    queue.Enqueue(file);
                }
                queue.Quit();
                Trace.WriteLine("insert file completed. Waiting for worker thread to complete");
                for (int i = 0; i < numberOfThread; i++)
                {
                    threads[i].Join();
                }
                Trace.WriteLine("Done insert file");
            }
            finally
            {

                queue.Quit();
                for (int i = 0; i < numberOfThread; i++)
                {
                    if (threads[i] != null)
                        if (threads[i].ThreadState == System.Threading.ThreadState.Running)
                            threads[i].Abort();
                }
            }
        }

        private ADItem Search(ADWebService adws, ADDomainInfo domainInfo, string userName)
        {
            ADItem output = null;
            WorkOnReturnedObjectByADWS callback =
                    (ADItem aditem) =>
                    {
                        output = aditem;
                    };

            if (userName.StartsWith("S-1-5"))
            {
                adws.Enumerate(domainInfo.DefaultNamingContext,
												"(objectSid=" + ADConnection.EncodeSidToString(userName) + ")",
												properties.ToArray(), callback);
				if (output != null)
					return output;
            }
			if (userName.StartsWith("CN=") && userName.EndsWith(domainInfo.DefaultNamingContext))
			{
				adws.Enumerate(domainInfo.DefaultNamingContext,
												"(distinguishedName=" + ADConnection.EscapeLDAP(userName) + ")",
												properties.ToArray(), callback);
				if (output != null)
					return output;
			}
			if (userName.Length <= 20)
			{
				adws.Enumerate(domainInfo.DefaultNamingContext,
												"(&(objectCategory=person)(objectClass=user)(sAMAccountName=" + ADConnection.EscapeLDAP(userName) + "))",
												properties.ToArray(), callback);
				if (output != null)
					return output;
			}
            adws.Enumerate(domainInfo.DefaultNamingContext,
											"(cn=" + ADConnection.EscapeLDAP(userName) + ")",
											properties.ToArray(), callback);
            if (output != null)
                return output;
            adws.Enumerate(domainInfo.DefaultNamingContext,
											"(displayName=" + ADConnection.EscapeLDAP(userName) + ")",
											properties.ToArray(), callback);
            if (output != null)
                return output;
            return output;
        }


		private List<DataStorageDomainTrusts> GetAllDomainTrusts(string server)
		{
			var output = new List<DataStorageDomainTrusts>();
			IntPtr ptr = IntPtr.Zero;
			uint DomainCount = 0;
			uint error = NativeMethods.DsEnumerateDomainTrusts(server, (uint)NativeMethods.DS_DOMAIN_TRUST_TYPE.ALL, out ptr, out DomainCount);
			if (error == 0)
			{
				for (int i = 0; i < DomainCount; i++)
				{
					IntPtr p = new IntPtr((ptr.ToInt64() + i * Marshal.SizeOf(typeof(NativeMethods.DS_DOMAIN_TRUSTS))));
					var domain = (NativeMethods.DS_DOMAIN_TRUSTS)Marshal.PtrToStructure(p, typeof(NativeMethods.DS_DOMAIN_TRUSTS));

					output.Add(new DataStorageDomainTrusts()
						{
							NetbiosDomainName = domain.NetbiosDomainName,
							DnsDomainName = domain.DnsDomainName,
							Flags = domain.Flags,
							ParentIndex = domain.ParentIndex,
							TrustType = domain.TrustType,
							TrustAttributes = domain.TrustAttributes,
							DomainSid = domain.DomainSid != IntPtr.Zero ? new SecurityIdentifier(domain.DomainSid) : null,
							DomainGuid = domain.DomainGuid,
						});
				}
				NativeMethods.NetApiBufferFree(ptr);
			}
			return output;
		}

		private void AddTrustedDomains(LiveDataStorage storage)
		{
			storage.KnownDomains.Clear();
			List<DataStorageDomainTrusts> domains;
			List<SecurityIdentifier> KnownSID = new List<SecurityIdentifier>();


			domains = GetAllDomainTrusts(Server);
			storage.KnownDomains.AddRange(domains);
			KnownSID.AddRange( domains.ConvertAll(x => x.DomainSid));

			var domainLocator = new DomainLocator(Server);
			foreach (var node in storage.nodes.Values)
			{
				if (!String.IsNullOrEmpty(node.Sid) && node.Sid.StartsWith("S-1-5-21-") && node.Shortname.Contains("\\"))
				{
					var sid = new SecurityIdentifier(node.Sid);
					var domainSid = sid.AccountDomainSid;
					if (!KnownSID.Contains(domainSid))
					{
						string domainName;
						string forestName;
						string NetbiosName = node.Shortname.Split('\\')[0];
						if (domainLocator.LocateDomainFromNetbios(NetbiosName, out domainName, out forestName))
						{
							KnownSID.Add(domainSid);
							storage.KnownDomains.Add(new DataStorageDomainTrusts()
							{
								DnsDomainName = domainName,
								DomainSid = domainSid,
								NetbiosDomainName = NetbiosName,
							}
							);
						}
					}
				}
			}
		}
    }
}
