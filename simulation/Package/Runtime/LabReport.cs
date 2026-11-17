using System.Linq;
using System.Net;

namespace Horse.MikuPhysicsLab
{
    internal static class LabReport
    {
        private static string Encode(string value) => WebUtility.HtmlEncode(value ?? "");

        internal static string Html(LabTestReport report)
        {
            string rows = string.Join("", report.cases.Select(c => "<tr><td>" + Encode(c.variant + (c.actualVariant == c.variant ? "" : " -> " + c.actualVariant)) + "</td><td>" + Encode(c.category + " / " + c.rig) + "</td><td>" + Encode(c.label + (c.baseMotion == null ? "" : " | base: " + c.baseMotion)) +
                "</td><td>" + c.frameRate + "</td><td>" + (c.passed ? "INTEGRITY OK" : "FAILED") + (c.visualReviewRequired ? " / VISUAL REVIEW" : "") + "</td><td>" + c.normalizedCoverage.ToString("P0") +
                "</td><td>" + c.samples + " / " + c.untrustedSamples + " / " + c.delayedSamples + "</td><td>" + (c.maximumAnchorError * 1000).ToString("F2") +
                "</td><td>" + c.bodyIntersectionCandidates + " / " + c.coatIntersectionCandidates + " / " + c.selfIntersectionCandidates +
                "</td><td>" + c.maximumRenderedStretch95.ToString("F3") + " / " + c.maximumRenderedStretch.ToString("F3") + " / " + c.maximumLengthStretch.ToString("F3") +
                "</td><td>" + c.matchedBindings + (c.inferredSettings ? " (inferred speed)" : "") + "</td><td>" + (c.bodySkinningError * 1000000).ToString("F2") +
                "</td><td>" + c.maximumHairBoneAngle.ToString("F2") + " / " + c.maximumGlideHairStep.ToString("F2") + "</td><td><a href='" + c.screenshot + "'><img loading='lazy' width='200' src='" + c.screenshot + "'></a></td></tr>"));
            int flagged = report.cases.Count(c => !c.passed);
            return "<!doctype html><meta charset='utf-8'><title>Miku Physics Lab</title><style>body{background:#151b24;color:#e4edf5;font:15px system-ui;padding:24px}td,th{padding:9px;border:1px solid #3a4c62}table{border-collapse:collapse}a{color:#3df1dc}input{padding:12px;width:50%}</style>" +
                "<h1>Miku Physics Lab: " + report.cases.Length + " cases / " + flagged + " flagged</h1><p>Unity " + report.unity + " | " + Encode(report.graphics) +
                "</p><p>Physics settings match: " + report.parity.settingsMatch + " | Driver: " + Encode(report.parity.animationDriver) + " | Rendered frames: " + report.renderedFrames +
                "</p><p>Catalog coverage: " + report.testedConfigurations + " / " + report.catalogConfigurations + " configurations. Complete cycles: " + report.cases.Count(c => c.completeClip) + "/" + report.cases.Length +
                "</p><p>" + Encode(report.limitation) + "</p><input id='search' placeholder='Filter model, animation, rig or status'><table id='cases'><thead><tr><th>Model</th><th>Category</th><th>Animation</th><th>Steps/s</th><th>Status</th><th>Cycle</th><th>Samples / untrusted / delayed</th><th>Anchor mm</th><th>Body / coat / self candidates</th><th>Fabric strain: edge p95 / max / waist-to-hem</th><th>Bindings</th><th>Skin error um</th><th>Hair angle / glide step deg</th><th>View</th></tr></thead><tbody>" + rows +
                "</tbody></table><h2>Runtime errors</h2><pre>" + Encode(string.Join("\n", report.errors)) + "</pre><script>document.querySelector('#search').oninput=function(){let q=this.value.toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(q))}</script>";
        }
    }
}
