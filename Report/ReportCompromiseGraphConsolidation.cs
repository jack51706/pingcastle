﻿using PingCastle.Data;
using PingCastle.template;
//
// Copyright (c) Ping Castle. All rights reserved.
// https://www.pingcastle.com
//
// Licensed under the Non-Profit OSL. See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace PingCastle.Report
{
	public class ReportCompromiseGraphConsolidation : ReportBase
	{
		private PingCastleReportCollection<CompromiseGraphData> Report;

		public string GenerateReportFile(PingCastleReportCollection<CompromiseGraphData> report, ADHealthCheckingLicense license, string filename)
		{
			Report = report;
			return GenerateReportFile(filename);
		}

		public string GenerateRawContent(PingCastleReportCollection<CompromiseGraphData> report, string selectedTab = null)
		{
			Report = report;
			sb.Length = 0;
			GenerateContent(selectedTab);
			return sb.ToString();
		}

		protected override void GenerateFooterInformation()
		{
			AddBeginScript();
			AddLine(TemplateManager.LoadJqueryDatatableJs());
			AddLine(TemplateManager.LoadDatatableJs());
			Add(@"
$('table').DataTable(
    {
        'paging': false,
        'searching': false
    }
);
$(function () {
	$('[data-toggle=""tooltip""]').tooltip({html: true, container: 'body'});
});
</script>
");
		}

		protected override void GenerateTitleInformation()
		{
			Add("PingCastle Consolidation report - ");
			Add(DateTime.Now.ToString("yyyy-MM-dd"));
		}

		protected override void GenerateHeaderInformation()
		{
			AddBeginStyle();
			AddLine(TemplateManager.LoadDatatableCss());
			AddLine(GetStyleSheetTheme());
			AddLine(@"</style>");
		}

		protected override void Hook(StringBuilder sbHtml)
		{
			sbHtml.Replace("<body>", @"<body data-spy=""scroll"" data-target="".navbar"" data-offset=""50"">");
		}

		protected override void GenerateBodyInformation()
		{
			Version version = Assembly.GetExecutingAssembly().GetName().Version;
			string versionString = version.ToString(4);
#if DEBUG
			versionString += " Beta";
#endif
			GenerateNavigation("Consolidation", null, DateTime.Now);
			GenerateAbout(@"<p><strong>Generated by <a href=""https://www.pingcastle.com"">Ping Castle</a> all rights reserved</strong></p>
<p>Open source components:</p>
<ul>
<li><a href=""https://getbootstrap.com/"">Bootstrap</a> licensed under the <a href=""https://tldrlegal.com/license/mit-license"">MIT license</a></li>
<li><a href=""https://datatables.net/"">DataTables</a> licensed under the <a href=""https://tldrlegal.com/license/mit-license"">MIT license</a></li>
<li><a href=""https://popper.js.org/"">Popper.js</a> licensed under the <a href=""https://tldrlegal.com/license/mit-license"">MIT license</a></li>
<li><a href=""https://jquery.org"">JQuery</a> licensed under the <a href=""https://tldrlegal.com/license/mit-license"">MIT license</a></li>
</ul>");
			Add(@"
<div id=""wrapper"" class=""container-fluid well"">
	<noscript>
		<div class=""alert alert-warning"">
			<p>PingCastle reports work best with Javascript enabled.</p>
		</div>
	</noscript>
<div class=""row""><div class=""col-lg-12""><h1>Consolidation</h1>
			<h3>Date: " + DateTime.Now.ToString("yyyy-MM-dd") + @" - Engine version: " + versionString + @"</h3>
</div></div>
");
			GenerateContent();
			Add(@"
</div>
");
		}

		void GenerateContent(string selectedTab = null)
		{
			Add(@"
<div class=""row"">
    <div class=""col-lg-12"">
		<ul class=""nav nav-tabs"" role=""tablist"">");
			GenerateTabHeader("Anomalies", selectedTab, true);
			GenerateTabHeader("Trusts", selectedTab);
			Add(@"
        </ul>
    </div>
</div>
<div class=""row"">
    <div class=""col-lg-12"">
		<div class=""tab-content"">");
			GenerateSectionFluid("Anomalies", GenerateAnomalies, selectedTab, true);
			GenerateSectionFluid("Trusts", GenerateTrusts, selectedTab);
			Add(@"
		</div>
	</div>
</div>");
		}

		private void GenerateAnomalies()
		{
			List<string> knowndomains = new List<string>();
			GenerateSubSection("Link with other domains");
			Add(@"
		<div class=""row"">
			<div class=""col-md-12 table-responsive"">
				<table class=""table table-striped table-bordered"">
					<thead><tr>
						<th  rowspan=""2"">Domain</th>
");
			int numRisk = 0;
			foreach (var objectRisk in (CompromiseGraphDataObjectRisk[])Enum.GetValues(typeof(CompromiseGraphDataObjectRisk)))
			{
				Add(@"<th colspan=""4"">");
				AddEncoded(ReportHelper.GetEnumDescription(objectRisk));
				Add(@"</th>");
				numRisk++;
			}
			Add(@"</tr>
");
			Add(@"<tr>");
			for (int i = 0; i < numRisk; i++)
			{
				Add(@"<th>Critical Object Found&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Indicates if critical objects such as everyone, authenticated users or domain users can take control, directly or not, of one of the objects."">?</i></th>
<th>Number of objects with Indirect&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Indicates the count of objects per category having at least one indirect user detected."">?</i></th>
<th>Max number of indirect numbers&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Indicates the maximum on all objects of the number of users having indirect access to the object."">?</i></th>
<th>Max ratio&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Indicates in percentage the value of (number of indirect users / number of direct users) if at least one direct users exists. Else the value is zero."">?</i></th>");
			}
			Add(@"</tr>");
			Add(@"
					</thead>
					<tbody>
");
			foreach (var data in Report)
			{
				Add(@"
				<tr>
					<td class='text'>" + PrintDomain(data.Domain) + @"</td>
");
				foreach (var objectRisk in (CompromiseGraphDataObjectRisk[])Enum.GetValues(typeof(CompromiseGraphDataObjectRisk)))
				{
					bool found = false;
					foreach (var analysis in data.AnomalyAnalysis)
					{
						if (analysis.ObjectRisk != objectRisk)
							continue;
						found = true;
						Add(@"<td class=""text"">");
						Add((analysis.CriticalObjectFound ? "<span class='unticked'>YES</span>" : "<span class='ticked'>NO</span>"));
						Add(@"</td><td class=""num"">");
						Add(analysis.NumberOfObjectsWithIndirect);
						Add(@"</td><td class=""num"">");
						Add(analysis.MaximumIndirectNumber);
						Add(@"</td><td class=""num"">");
						Add(analysis.MaximumDirectIndirectRatio);
						Add(@"</td>");
						break;
					}
					if (!found)
					{
						Add(@"<td></td><td></td><td></td><td></td>");
					}
				}
				Add(@"</tr>
");
			}
			Add(@"
					</tbody>
				</table>
			</div>
		</div>
");
		}

		private void GenerateTrusts()
		{
			List<string> knowndomains = new List<string>();
			GenerateSubSection("Link with other domains");
			Add(@"
		<div class=""row"">
			<div class=""col-md-12 table-responsive"">
				<table class=""table table-striped table-bordered"">
					<thead><tr>
						<th  rowspan=""2"">Domain</th>
						<th rowspan=""2"">Remote Domain</th>
");
			int numTypology = 0;
			foreach (var typology in (CompromiseGraphDataTypology[])Enum.GetValues(typeof(CompromiseGraphDataTypology)))
			{
				Add(@"<th colspan=""3"">");
				AddEncoded(ReportHelper.GetEnumDescription(typology));
				Add(@"</th>");
				numTypology++;
			}
			Add(@"</tr>
");
			Add(@"<tr>");
			for (int i = 0; i < numTypology; i++)
			{
				Add(@"<th>Group&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Number of group impacted by this domain"">?</i></th><th>Resolved&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Number of unique SID (account, group, computer, ...) resolved"">?</i></th><th>Unresolved&nbsp;<i class=""info-mark"" data-placement=""bottom"" data-toggle=""tooltip"" title="""" data-original-title=""Number of unique SID (account, group, computer, ...) NOT resolved meaning that the underlying object may have been removed"">?</i></th>");
			}
			Add(@"</tr>");
			Add(@"
					</thead>
					<tbody>
");
			foreach (var data in Report)
			{

				if (!knowndomains.Contains(data.DomainFQDN))
					knowndomains.Add(data.DomainFQDN);

				foreach (var dependancy in data.Dependancies)
				{
					if (!knowndomains.Contains(dependancy.FQDN))
						knowndomains.Add(dependancy.FQDN);
					Add(@"
						<tr>
							<td class='text'>" + PrintDomain(data.Domain) + @"</td>
							<td class='text'>" + PrintDomain(dependancy.Domain) + @"</td>
");
					foreach (var typology in (CompromiseGraphDataTypology[])Enum.GetValues(typeof(CompromiseGraphDataTypology)))
					{
						bool found = false;
						foreach (var item in dependancy.Details)
						{
							if (item.Typology != typology)
								continue;
							found = true;
							Add(@"<td class=""num"">");
							Add(item.NumberOfGroupImpacted);
							Add(@"</td><td class=""num"">");
							Add(item.NumberOfResolvedItems);
							Add(@"</td><td class=""num"">");
							Add(item.NumberOfUnresolvedItems);
							Add(@"</td>");
							break;
						}
						if (!found)
						{
							Add(@"<td></td><td></td><td></td>");
						}
					}
					Add(@"</tr>
");
				}
			}
			Add(@"
					</tbody>
				</table>
			</div>
		</div>
");
		}
	}
}
