namespace BuildingBlocks.Configuration;

/// <summary>
/// Local-development convenience: loads KEY=VALUE pairs from the nearest <c>.env</c> file
/// (searching upwards from the binary) into process environment variables.
/// Variables already set in the real environment always win, so containers are unaffected.
/// </summary>
public static class DotEnv
{
    public static void Load(string fileName = ".env", int maxLevelsUp = 6)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; dir is not null && level <= maxLevelsUp; level++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');
                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            return;
        }
    }
}
