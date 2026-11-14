using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Flashpi.Tools.OpenApi;

namespace Flashpi.Tools.Coverage;

/// <summary>
/// Fails when the line coverage of the SDK's hand-written code is under a minimum:
/// <c>coverage check &lt;directory with a Cobertura report&gt; [percent]</c> (95 by default). The
/// generated models are left out of the report (<c>--coverlet-exclude-by-attribute GeneratedCode</c>).
/// </summary>
internal static class CoverageCommand
{
    public static int Check(string directory, string minimum)
    {
        var report = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.cobertura*.xml", SearchOption.AllDirectories).OrderBy(File.GetLastWriteTimeUtc).LastOrDefault()
            : null;
        if (report is null)
        {
            throw new GenerationException($"No Cobertura report in {directory}");
        }
        var required = double.Parse(minimum, CultureInfo.InvariantCulture);
        var rate = double.Parse(XDocument.Load(report).Root!.Attribute("line-rate")!.Value, CultureInfo.InvariantCulture) * 100;
        var summary = string.Create(CultureInfo.InvariantCulture, $"Line coverage {rate:0.0} % (minimum {required:0.#} %)");
        if (rate < required)
        {
            Console.Error.WriteLine($"error: {summary}");
            return 1;
        }
        Console.WriteLine(summary);
        return 0;
    }
}
