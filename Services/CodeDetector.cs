using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClipVault.Services;

/// <summary>
/// Cheap heuristic that decides whether a text clip is source code (or JSON / markup / SQL / shell)
/// rather than prose, so the UI can show it in a monospace font. No language detection, no parsing.
/// </summary>
public static partial class CodeDetector
{
    private static readonly string[] KeywordStarts =
    {
        "using ", "import ", "from ", "def ", "class ", "public ", "private ", "protected ", "internal ", "static ",
        "function ", "func ", "fn ", "pub ", "impl ", "const ", "let ", "var ", "return", "if ", "if(", "else", "elif ",
        "for ", "for(", "foreach ", "while ", "while(", "switch ", "case ", "try", "catch", "finally", "namespace ",
        "interface ", "struct ", "enum ", "void ", "int ", "string ", "bool ", "double ", "float ", "async ", "await ",
        "export ", "package ", "module ", "require(", "#include", "#!/", "#define", "#pragma", "<?xml", "<!DOCTYPE",
        "@echo", "echo ", "set ", "$ ", "select ", "insert ", "update ", "delete ", "create ", "alter ", "drop ",
        "where ", "group by", "order by", "inner join", "left join", "begin", "end;", "end.", "then", "throw ", "raise ",
        "yield ", "print(", "console.", "System.", "self.", "this.", "@", "-- ", "// ", "/* ", "* ", "*/", "# ",
    };

    private static readonly string[] CommandStarts =
    {
        "git ", "dotnet ", "npm ", "npx ", "yarn ", "pnpm ", "pip ", "python ", "node ", "docker ", "kubectl ", "curl ",
        "wget ", "cd ", "ls ", "dir ", "cat ", "grep ", "sed ", "awk ", "ssh ", "scp ", "powershell ", "pwsh ", "sudo ",
        "apt ", "brew ", "choco ", "winget ", "cargo ", "go ", "mvn ", "gradle ", "flutter ", "adb ",
    };

    [GeneratedRegex(@"^\s*""[^""]+""\s*:")] private static partial Regex JsonKey();
    [GeneratedRegex(@"^[\w.\[\]$@]+\s*(=|\+=|-=|:=|==|\+\+|--)")] private static partial Regex Assignment();
    [GeneratedRegex(@"^</?[A-Za-z][\w:-]*(\s[^>]*)?/?>")] private static partial Regex Tag();
    [GeneratedRegex(@"^[\w.:$@]+\s*\(.*\)\s*[;{]?$")] private static partial Regex Call();
    [GeneratedRegex(@"[A-Za-z]\s[a-z]+\s[a-z]+\s[a-z]+")] private static partial Regex ProseWords();
    [GeneratedRegex(@"^[\w.-]+:$")] private static partial Regex YamlKey();
    [GeneratedRegex(@"^-\s+[\w.-]+:\s")] private static partial Regex YamlListItem();

    public static bool LooksLikeCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0) return false;

        if (lines.Length == 1) return SingleLineLooksLikeCode(lines[0].Trim());

        int hits = 0, indented = 0, prose = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (raw.StartsWith("  ") || raw.StartsWith('\t')) indented++;
            if (LineLooksLikeCode(raw, line)) hits++;
            // Sentences: a run of lowercase words and a terminal period, with no code punctuation.
            if (line.EndsWith('.') && ProseWords().IsMatch(line) && !line.Any(c => c is ';' or '{' or '}' or '=' or '<' or '>'))
                prose++;
        }

        if (prose > lines.Length / 2) return false;
        // Either most lines carry code markers, or the block is clearly indented with some markers.
        return hits * 2 >= lines.Length || (indented * 2 >= lines.Length && hits >= 2);
    }

    private static bool LineLooksLikeCode(string raw, string line)
    {
        if (line.EndsWith(';') || line.EndsWith('{') || line.EndsWith('}') || line == "}" || line.EndsWith("},") ||
            line.EndsWith("],") || line.EndsWith("):") || line.EndsWith("=>") || line.EndsWith("{}") || line.EndsWith("()"))
            return true;
        if (line.StartsWith('}') || line.StartsWith('{') || line.StartsWith(']') || line.StartsWith('[') || line.StartsWith("<"))
            return true;
        if (StartsWithAny(line, KeywordStarts) || StartsWithAny(line, CommandStarts)) return true;
        if (JsonKey().IsMatch(line) || Assignment().IsMatch(line) || Tag().IsMatch(line) || Call().IsMatch(line)) return true;
        if (YamlKey().IsMatch(line) || YamlListItem().IsMatch(line)) return true;
        if (line.Contains("=>") || line.Contains("->") || line.Contains("::") || line.Contains("!=") || line.Contains("==") ||
            line.Contains("&&") || line.Contains("||") || line.Contains("();") || line.Contains(");"))
            return true;
        return false;
    }

    private static bool SingleLineLooksLikeCode(string line)
    {
        if (line.Length < 4) return false;
        if (StartsWithAny(line, CommandStarts)) return true;
        if (line.StartsWith("select ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("insert ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("update ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("delete from ", StringComparison.OrdinalIgnoreCase))
            return true;
        if ((line.StartsWith('{') && line.EndsWith('}')) || (line.StartsWith('[') && line.EndsWith(']'))) return line.Contains(':') || line.Contains(',');
        if (Tag().IsMatch(line) && line.EndsWith('>')) return true;
        if (line.EndsWith(';') || line.EndsWith('{')) return true;
        if (line.Contains("=>") || line.Contains("();") || line.Contains("::")) return true;
        if (Call().IsMatch(line) && !line.Contains(' ' ) ) return true;
        if (Assignment().IsMatch(line) && !ProseWords().IsMatch(line)) return true;
        return false;
    }

    private static bool StartsWithAny(string line, string[] prefixes)
    {
        foreach (var p in prefixes)
            if (line.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
