// SPDX-License-Identifier: UNLICENSED
// Oasis.SurrealDb.Schema.Tests -- LOW #L3 regression: MermaidParser must
// not regress to O(N^2) total parse time as the source grows. The previous
// implementation rewound the cursor by re-scanning from offset 0 for every
// internal speculative read, which compounded into a quadratic hot path
// across files containing many entities. The Savepoint-based Restore path
// is O(1), so a synthetic 500-entity schema must parse in well under a
// second on developer hardware.

using System;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Oasis.SurrealDb.Schema.Mermaid;

namespace Oasis.SurrealDb.Schema.Tests
{
    public class MermaidParserPerformanceTests
    {
        [Fact]
        public void Parses_500_entity_schema_in_under_one_second()
        {
            var src = BuildSyntheticSchema(entityCount: 500);

            var sw = Stopwatch.StartNew();
            var model = MermaidParser.Parse(src, "perf-synthetic");
            sw.Stop();

            model.Entities.Should().HaveCount(500,
                "synthetic schema must round-trip through the parser without entity loss");

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
                $"L3 fix: 500-entity schema parsed in {sw.ElapsedMilliseconds}ms; budget is 1000ms");
        }

        private static string BuildSyntheticSchema(int entityCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("erDiagram");
            for (int i = 0; i < entityCount; i++)
            {
                // Each entity carries a couple of annotations + a few fields
                // so the parser exercises the same speculative-read paths
                // (annotation lookahead, identifier rewind) that were
                // quadratic before the fix.
                sb.AppendLine("    %% @surreal.schemafull");
                sb.AppendLine($"    %% @surreal.aggregate \"Entity{i} (synthetic)\"");
                sb.AppendLine($"    entity_{i} {{");
                sb.AppendLine("        %% @surreal.assert \"$value != NONE\"");
                sb.AppendLine("        string id");
                sb.AppendLine("        string name");
                sb.AppendLine("        int count");
                sb.AppendLine("        datetime created_at");
                sb.AppendLine("    }");
            }
            return sb.ToString();
        }
    }
}
