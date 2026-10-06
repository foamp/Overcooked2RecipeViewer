using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace Overcooked2RecipeViewer
{
    // Uses only mscorlib so the game's older Mono runtime needs no extra DLLs.
    internal sealed class RecipeLayoutStore
    {
        private const string Header = "RecipeLayoutV1";

        private sealed class NamedLayout
        {
            internal string Name;
            internal List<List<string>> Rows;
        }

        private sealed class LevelLayouts
        {
            internal string Key;
            internal List<List<string>> Draft;
            internal readonly List<NamedLayout> Saved = new List<NamedLayout>();
        }

        private readonly string _path;
        private readonly string _levelKey;
        private readonly ManualLogSource _logger;
        private readonly List<LevelLayouts> _levels = new List<LevelLayouts>();

        internal string Path { get { return _path; } }

        internal RecipeLayoutStore(string levelKey, ManualLogSource logger)
            : this(levelKey, logger, null)
        {
        }

        internal RecipeLayoutStore(string levelKey, ManualLogSource logger, string pathOverride)
        {
            _levelKey = levelKey;
            _logger = logger;
            if (pathOverride == null)
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                    appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // Keep existing users' saved layouts without changing their storage location.
                _path = System.IO.Path.Combine(appData,
                    System.IO.Path.Combine("Overcooked2RecipePreview", "layouts.txt"));
            }
            else _path = pathOverride;
            Load();
        }

        internal List<List<string>> ReadDraft()
        {
            LevelLayouts level = FindLevel(false);
            return level == null ? null : level.Draft;
        }

        internal List<string> SavedNames()
        {
            List<string> names = new List<string>();
            LevelLayouts level = FindLevel(false);
            if (level == null) return names;
            for (int i = 0; i < level.Saved.Count; i++)
                names.Add(level.Saved[i].Name);
            return names;
        }

        internal List<List<string>> ReadSaved(string name)
        {
            LevelLayouts level = FindLevel(false);
            if (level == null) return null;
            for (int i = 0; i < level.Saved.Count; i++)
                if (level.Saved[i].Name == name) return level.Saved[i].Rows;
            return null;
        }

        internal void SaveDraft(List<List<string>> rows)
        {
            FindLevel(true).Draft = rows;
            Persist();
        }

        internal string SaveNamed(List<List<string>> rows)
        {
            LevelLayouts level = FindLevel(true);
            int number = 1;
            while (ReadSaved("Saved " + number) != null) number++;
            NamedLayout saved = new NamedLayout();
            saved.Name = "Saved " + number;
            saved.Rows = rows;
            level.Saved.Add(saved);
            Persist();
            return saved.Name;
        }

        internal bool DeleteSaved(string name)
        {
            LevelLayouts level = FindLevel(false);
            if (level == null) return false;
            for (int i = 0; i < level.Saved.Count; i++)
            {
                if (level.Saved[i].Name != name) continue;
                NamedLayout removed = level.Saved[i];
                level.Saved.RemoveAt(i);
                try { Persist(); }
                catch
                {
                    level.Saved.Insert(i, removed);
                    throw;
                }
                return true;
            }
            return false;
        }

        private LevelLayouts FindLevel(bool create)
        {
            for (int i = 0; i < _levels.Count; i++)
                if (_levels[i].Key == _levelKey) return _levels[i];
            if (!create) return null;
            LevelLayouts level = new LevelLayouts();
            level.Key = _levelKey;
            _levels.Add(level);
            return level;
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                string[] lines = File.ReadAllLines(_path, Encoding.UTF8);
                if (lines.Length == 0 || lines[0] != Header)
                    throw new FormatException("Unexpected layout file header.");
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;
                    string[] fields = lines[i].Split('\t');
                    if (fields.Length < 3) continue;
                    string key = Decode(fields[1]);
                    LevelLayouts level = FindLevelByKey(key);
                    if (fields[0] == "D" && fields.Length == 3)
                        level.Draft = DecodeRows(fields[2]);
                    else if (fields[0] == "S" && fields.Length == 4)
                    {
                        NamedLayout saved = new NamedLayout();
                        saved.Name = Decode(fields[2]);
                        saved.Rows = DecodeRows(fields[3]);
                        level.Saved.Add(saved);
                    }
                }
            }
            catch (Exception exception)
            {
                _levels.Clear();
                _logger.LogWarning("Could not read saved recipe layouts: " + exception);
            }
        }

        private LevelLayouts FindLevelByKey(string key)
        {
            for (int i = 0; i < _levels.Count; i++)
                if (_levels[i].Key == key) return _levels[i];
            LevelLayouts level = new LevelLayouts();
            level.Key = key;
            _levels.Add(level);
            return level;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static string EncodeRows(List<List<string>> rows)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) result.Append(';');
                for (int j = 0; j < rows[i].Count; j++)
                {
                    if (j > 0) result.Append(',');
                    result.Append(Encode(rows[i][j]));
                }
            }
            return result.ToString();
        }

        private static List<List<string>> DecodeRows(string value)
        {
            List<List<string>> rows = new List<List<string>>();
            if (value.Length == 0) return rows;
            string[] rawRows = value.Split(';');
            for (int i = 0; i < rawRows.Length; i++)
            {
                if (rawRows[i].Length == 0) continue;
                List<string> row = new List<string>();
                string[] cards = rawRows[i].Split(',');
                for (int j = 0; j < cards.Length; j++)
                    row.Add(Decode(cards[j]));
                rows.Add(row);
            }
            return rows;
        }

        private void Persist()
        {
            string directory = System.IO.Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            try
            {
                using (StreamWriter writer = new StreamWriter(temporary, false,
                    new UTF8Encoding(false)))
                {
                    writer.WriteLine(Header);
                    for (int i = 0; i < _levels.Count; i++)
                    {
                        LevelLayouts level = _levels[i];
                        if (level.Draft != null)
                            writer.WriteLine("D\t" + Encode(level.Key) + "\t" +
                                EncodeRows(level.Draft));
                        for (int j = 0; j < level.Saved.Count; j++)
                            writer.WriteLine("S\t" + Encode(level.Key) + "\t" +
                                Encode(level.Saved[j].Name) + "\t" +
                                EncodeRows(level.Saved[j].Rows));
                    }
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
