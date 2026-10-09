using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Overcooked2RecipeViewer
{
    // No Unity native calls or clipboard mutations. Exercises production code.
    internal static class ExportTests
    {
        private static int _checks;
        private static string _output, _core;
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL: " + name); _checks++; }
        private static void Expect<T>(Action action, string name) where T : Exception
        {
            try { action(); } catch (T) { _checks++; return; }
            throw new Exception("FAIL (no expected exception): " + name);
        }
        private static void Equal(byte[] actual, byte[] expected, string name)
        {
            Check(actual.Length == expected.Length, name + " length");
            for (int i = 0; i < actual.Length; i++) Check(actual[i] == expected[i], name + " byte " + i);
        }
        private static void Main(string[] args)
        {
            _output = args[0]; _core = args[1];
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string path = Path.Combine(_core, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            // The game loader normally initializes this path. Keep the real
            // ConfigFile's global test config inside ignored test artifacts.
            Type paths = Assembly.LoadFrom(Path.Combine(_core, "BepInEx.dll")).GetType("BepInEx.Paths");
            paths.GetProperty("BepInExConfigPath").GetSetMethod(true).Invoke(null,
                new object[] { Path.Combine(_output, "BepInEx-test.cfg") });
            try { Run(); Console.WriteLine("PASS: " + _checks + " assertions; fixtures: " + _output); }
            catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        }
        private static void Run()
        {
            Directory.CreateDirectory(_output);
            ExportSettings bad = new ExportSettings();
            bad.Background = (ExportBackground)99; bad.Margin = (ExportMargin)99; bad.Layout = (ExportLayout)99;
            bad.Normalize();
            Check(bad.Background == ExportBackground.Default && bad.Margin == ExportMargin.Standard && bad.Layout == ExportLayout.Standard, "invalid settings fallback");
            Check(!RecipeExport.SafeDimensions(double.NaN, 1), "NaN size");
            Check(!RecipeExport.SafeDimensions(12001, 1), "width bound");
            Check(!RecipeExport.SafeDimensions(1, 100001), "height bound");
            Check(!RecipeExport.SafeDimensions(12000, 100000), "pixel budget");
            Check(RecipeExport.SafeDimensions(12000, 10000), "safe long PNG");
            TestAlpha(); TestDib(); TestComposite(); TestCropAndRows(); TestManualRows(); TestPngFailures(); TestLongPng(); TestLayouts(); TestConfig(); TestInterface();
        }
        private static void TestAlpha()
        {
            byte[] black = { 0,0,0,255, 120,60,30,255, 200,80,20,255 };
            byte[] white = { 255,255,255,255, 247,187,157,255, 200,80,20,255 };
            RecipeExport.RecoverAlpha(black, white, false);
            Equal(black, new byte[] { 0,0,0,0, 239,120,60,128, 200,80,20,255 }, "straight gamma alpha");
            for (int linear = 0; linear < 2; linear++)
            {
                for (int a = 16; a <= 255; a += 31)
                {
                    double alpha = a / 255.0;
                    byte[] b = new byte[4], w = new byte[4];
                    int[] original = { 201, 83, 137 };
                    for (int c = 0; c < 3; c++)
                    {
                        double v = original[c] / 255.0;
                        if (linear == 1) v = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                        b[c] = Matte(v * alpha, linear == 1);
                        w[c] = Matte(v * alpha + 1 - alpha, linear == 1);
                    }
                    RecipeExport.RecoverAlpha(b, w, linear == 1);
                    Check(Math.Abs(b[3] - a) <= 2, "matte recovered alpha");
                    for (int c = 0; c < 3; c++) Check(Math.Abs(b[c] - original[c]) <= (a < 48 ? 16 : 7), "matte recovered edge color");
                }
            }
            Expect<ArgumentException>(delegate { RecipeExport.RecoverAlpha(new byte[1], new byte[2], false); }, "invalid matte buffer");
        }
        private static byte Matte(double v, bool linear)
        {
            if (linear) v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Round(v * 255);
        }
        private static void TestDib()
        {
            byte[] pixel = { 200, 100, 50, 128 };
            byte[] alpha = ClipboardImage.BuildDib(pixel, 1, 1, true);
            Check(BitConverter.ToInt32(alpha, 0) == 124 && alpha.Length == 128, "V5 header");
            Check(BitConverter.ToInt32(alpha, 52) == unchecked((int)0xFF000000), "V5 alpha mask");
            Equal(new byte[] { alpha[124], alpha[125], alpha[126], alpha[127] }, new byte[] { 50,100,200,128 }, "V5 BGRA");
            byte[] legacy = ClipboardImage.BuildDib(pixel, 1, 1, false);
            Equal(new byte[] { legacy[40], legacy[41], legacy[42], legacy[43] }, new byte[] { 152,177,227,255 }, "legacy white matte");
        }
        private static void TestComposite()
        {
            byte[] card = { 0,0,255,255, 200,100,30,0, 255,0,0,255, 0,255,0,128 };
            for (int layout = 0; layout < 4; layout++)
            using (RecipeExport cache = new RecipeExport())
            {
                cache.Add(0, 0, 2, 2, card); cache.Add(3, 2, 2, 2, card);
                for (int background = 0; background < 5; background++)
                for (int margin = 0; margin < 4; margin++)
                {
                    ExportSettings settings = new ExportSettings();
                    settings.Layout = (ExportLayout)layout; settings.Background = (ExportBackground)background;
                    settings.Margin = (ExportMargin)margin; settings.Red = 80; settings.Green = 120; settings.Blue = 160;
                    int width, height;
                    Check(cache.Dimensions(settings, out width, out height), "safe fixture dimensions");
                    int pad = settings.Padding;
                    int gap = layout == 1 ? 12 : layout == 2 ? 32 : 64;
                    Check(width == (layout == 0 ? 5 : 4 + gap) + pad * 2 && height == (layout == 0 ? 4 : 2) + pad * 2, "native 1x margins and reflow dimensions");
                    string name = "case-" + layout + "-" + background + "-" + margin;
                    using (ExportScanlines lines = cache.OpenScanlines(settings))
                    {
                        PngStreamWriter.Save(Path.Combine(_output, name + ".png"), width, height, lines.Fill);
                        byte[] row = new byte[width * 4 + 1];
                        lines.Fill(pad, row);
                        Equal(new byte[] { row[1 + pad*4], row[2 + pad*4], row[3 + pad*4], row[4 + pad*4] }, new byte[] { 255,0,0,255 }, "top-down red");
                        if (background == 1)
                            Equal(new byte[] { row[5 + pad*4], row[6 + pad*4], row[7 + pad*4], row[8 + pad*4] }, new byte[] { 0,255,0,128 }, "half alpha preserved");
                    }
                    int tw, th;
                    byte[] thumb = cache.Thumbnail(settings, 2048, 2000000, out tw, out th);
                    Check(tw == width && th == height, "small preview unchanged");
                    File.WriteAllBytes(Path.Combine(_output, name + ".rgba"), thumb);
                    using (ExportThumbnail region = new ExportThumbnail(cache, settings, pad, pad, 2, 2, 2, 2))
                    {
                        while (region.NextRow()) { }
                        int offset = ((height - 1 - pad) * width + pad) * 4;
                        Equal(new byte[] { region.Pixels[8], region.Pixels[9], region.Pixels[10], region.Pixels[11] },
                            new byte[] { thumb[offset], thumb[offset+1], thumb[offset+2], thumb[offset+3] }, "detail matches full compositor");
                    }
                }
            }
            RecipeExport disposable = new RecipeExport();
            disposable.Add(0,0,2,2,card);
            string directory = Path.GetDirectoryName(disposable.Cards[0].Path);
            ExportSettings cancelSettings = new ExportSettings(); cancelSettings.Margin = ExportMargin.None;
            using (ExportThumbnail interrupted = new ExportThumbnail(disposable, cancelSettings, 2048, 2000000))
                Check(interrupted.NextRow(), "thumbnail started before cancellation");
            Expect<InvalidOperationException>(delegate { new ExportThumbnail(disposable, cancelSettings, 1, 1, 2, 2, 2, 2); }, "out-of-content detail region rejected");
            disposable.Dispose(); disposable.Dispose();
            Check(!Directory.Exists(directory), "cache cleanup idempotent");
            Expect<ObjectDisposedException>(delegate { disposable.Add(0,0,2,2,card); }, "closed cache rejected");
            using (RecipeExport overlap = new RecipeExport())
            {
                overlap.Add(0,0,1,1,new byte[] {255,0,0,128});
                overlap.Add(0,0,1,1,new byte[] {0,0,255,128});
                ExportSettings settings = new ExportSettings(); settings.Background = ExportBackground.Transparent; settings.Margin = ExportMargin.None;
                settings.Layout = ExportLayout.Current; // Internal exact-position compositor regression.
                using (ExportScanlines rows = overlap.OpenScanlines(settings))
                {
                    byte[] row = new byte[5]; rows.Fill(0,row);
                    Equal(row, new byte[] {0,85,0,170,192}, "overlapping straight alpha");
                }
            }
        }
        private static byte[] Solid(int width, int height, byte red)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = red; pixels[i + 3] = 255; }
            return pixels;
        }
        private static void TestCropAndRows()
        {
            using (RecipeExport cache = new RecipeExport())
            {
                byte[] pixels = new byte[5 * 6 * 4];
                // Opaque top right, alpha=1 shadow at bottom left: neither lost.
                pixels[(4 * 5 + 3) * 4] = 255; pixels[(4 * 5 + 3) * 4 + 3] = 255;
                pixels[(1 * 5 + 1) * 4 + 2] = 200; pixels[(1 * 5 + 1) * 4 + 3] = 1;
                pixels[0] = 99; // Invisible RGB must not expand the bounds.
                cache.Add(10, 20, 5, 6, pixels);
                ExportCard card = cache.Cards[0];
                Check(card.Left == 11 && card.Top == 21 && card.Width == 3 && card.Height == 4, "alpha crop adjusts top-down position");
                byte[] trimmed = File.ReadAllBytes(card.Path);
                Check(trimmed.Length == 48 && trimmed[3] == 1 && trimmed[2] == 200 && trimmed[44] == 255 && trimmed[47] == 255, "faint shadow and top artwork preserved");
                ExportSettings settings = new ExportSettings(); settings.Margin = ExportMargin.None; settings.Background = ExportBackground.Transparent;
                int w, h; Check(cache.Dimensions(settings, out w, out h) && w == 3 && h == 4, "outer blank envelope removed");
                using (ExportScanlines rows = cache.OpenScanlines(settings))
                {
                    byte[] row = new byte[w * 4 + 1]; rows.Fill(0, row);
                    Check(row[9] == 255 && row[12] == 255, "cropped top row orientation");
                    rows.Fill(3, row); Check(row[3] == 200 && row[4] == 1, "cropped bottom shadow orientation");
                }
                SaveFixture(cache, settings, "case-cropped");
                Expect<InvalidOperationException>(delegate { cache.Add(0,0,4,4,new byte[64]); }, "empty capture rejected");
                Check(cache.Cards.Count == 1, "failed empty capture leaves cache intact");
                int previous = -1;
                for (int margin = 0; margin < 4; margin++)
                {
                    settings.Margin = (ExportMargin)margin; cache.Dimensions(settings, out w, out h);
                    Check(w > previous && w - 3 == h - 4, "four margins grow visibly and equally"); previous = w;
                }
            }
            using (RecipeExport cache = new RecipeExport())
            {
                // Reading order deliberately differs from original coordinates.
                cache.Add(1000,700,780,120,Solid(780,120,10),0);
                cache.Add(0,0,780,80,Solid(780,80,20),0);
                cache.Add(100,400,300,60,Solid(300,60,30),1);
                ExportSettings settings = new ExportSettings(); settings.Margin = ExportMargin.None;
                settings.Layout = ExportLayout.Compact;
                ExportLayoutPlan compact = cache.CreatePlan(settings);
                Check(compact.Width == 1572 && compact.Height == 192, "compact preserves manual rows with tallest row height");
                Check(compact.Cards[0].Left == 0 && compact.Cards[1].Left == 792 && compact.Cards[1].Top == 0, "compact horizontal gap and reading order");
                Check(compact.Cards[2].Left == 0 && compact.Cards[2].Top == 132, "short manual row retained and left aligned");
                settings.Layout = ExportLayout.Standard;
                ExportLayoutPlan standard = cache.CreatePlan(settings);
                Check(standard.Width == 1592 && standard.Height == 212 && standard.Cards[2].Top == 152, "standard row and card spacing");
                settings.Layout = ExportLayout.Wide;
                ExportLayoutPlan wide = cache.CreatePlan(settings);
                Check(wide.Width == 1624 && wide.Height == 244 && wide.Cards[1].Top == 0 && wide.Cards[2].Top == 184, "wide spacing does not wrap manual row past 1600");
                foreach (ExportLayoutPlan plan in new ExportLayoutPlan[] { compact, standard, wide })
                {
                    for (int i = 0; i < plan.Cards.Count; i++)
                    {
                        Check(plan.Cards[i].Path == cache.Cards[i].Path && plan.Cards[i].Width == cache.Cards[i].Width && plan.Cards[i].Height == cache.Cards[i].Height && plan.Cards[i].Row == cache.Cards[i].Row, "spacing preserves every native card, row and order");
                        for (int j = i + 1; j < plan.Cards.Count; j++)
                        {
                            ExportCard a = plan.Cards[i], b = plan.Cards[j];
                            Check(a.Left + a.Width <= b.Left || b.Left + b.Width <= a.Left || a.Top + a.Height <= b.Top || b.Top + b.Height <= a.Top, "spaced cards never overlap");
                        }
                    }
                }
                settings.Layout = ExportLayout.Current;
                ExportLayoutPlan restored = cache.CreatePlan(settings);
                Check(cache.Cards[0].Left == 1000 && cache.Cards[0].Top == 700 && restored.Cards[0].Left == 1000 && restored.Cards[0].Top == 700, "switching layout restores original positions");
                settings.Background = ExportBackground.Transparent;
                for (int layout = 0; layout < 4; layout++)
                {
                    settings.Layout = (ExportLayout)layout;
                    ExportLayoutPlan plan = cache.CreatePlan(settings);
                    using (ExportScanlines rows = cache.OpenScanlines(settings))
                    {
                        byte[] row = new byte[plan.Width * 4 + 1];
                        for (int i = 0; i < plan.Cards.Count; i++)
                        {
                            rows.Fill(plan.Cards[i].Top, row);
                            Check(row[1 + plan.Cards[i].Left * 4] == (i + 1) * 10 && row[4 + plan.Cards[i].Left * 4] == 255, "spaced compositor preserves card identity and reading order");
                        }
                    }
                    SaveFixture(cache, settings, "case-spacing-" + layout);
                }
            }
            using (RecipeExport cache = new RecipeExport())
            {
                cache.Add(0,0,1601,1,Solid(1601,1,1)); cache.Add(0,100,1,1,Solid(1,1,2));
                ExportSettings settings = new ExportSettings(); settings.Layout = ExportLayout.Compact; settings.Margin = ExportMargin.None;
                ExportLayoutPlan plan = cache.CreatePlan(settings);
                Check(plan.Width == 1614 && plan.Cards[1].Top == 0 && plan.Cards[1].Left == 1613, "wide manual row never automatically wraps or clips");
            }
            using (RecipeExport cache = new RecipeExport())
            {
                cache.Add(0,0,1,1,Solid(1,1,1)); cache.Add(12000,100000,1,1,Solid(1,1,2));
                ExportSettings settings = new ExportSettings(); settings.Margin = ExportMargin.None;
                settings.Layout = ExportLayout.Current;
                int w,h; Check(!cache.Dimensions(settings,out w,out h), "unsafe current layout rejected");
                settings.Layout = ExportLayout.Compact;
                Check(cache.Dimensions(settings,out w,out h) && w == 14 && h == 1, "spacing can recover excessive whitespace without changing rows");
            }
        }
        private static void TestManualRows()
        {
            // Same group sizes as the user's 34-card / 9-row screenshot.
            // Native capture rows are recorded before cropping, not guessed
            // from post-crop Top coordinates or a maximum image width.
            int[] counts = {3,3,3,4,4,5,3,4,5};
            using (RecipeExport cache = new RecipeExport())
            {
                int identity = 0;
                for (int r = 0; r < counts.Length; r++)
                    for (int c = 0; c < counts[r]; c++)
                    {
                        int width = 100 + c * 8, height = 64 + c * 3;
                        byte[] pixels = Solid(width,height,(byte)++identity);
                        // A transparent top row changes Top after alpha crop.
                        if (c % 2 == 0)
                            for (int x = 0; x < width; x++) pixels[((height - 1) * width + x) * 4 + 3] = 0;
                        cache.Add(c * 150,r * 100,width,height,pixels,r);
                    }
                Check(identity == 34 && cache.Cards[0].Top != cache.Cards[1].Top, "manual row metadata survives unequal alpha crop offsets");
                ExportSettings settings = new ExportSettings(); settings.Margin = ExportMargin.None;
                settings.Background = ExportBackground.Transparent;
                for (int layout = 0; layout < 4; layout++)
                {
                    settings.Layout = (ExportLayout)layout;
                    ExportLayoutPlan plan = cache.CreatePlan(settings);
                    Check(plan.Cards.Count == 34, "all 34 manual cards preserved");
                    int index = 0, previousTop = -1;
                    for (int r = 0; r < counts.Length; r++)
                    {
                        int rowTop = plan.Cards[index].Top;
                        Check(rowTop > previousTop, "nine manual rows remain in original order");
                        previousTop = rowTop;
                        for (int c = 0; c < counts[r]; c++, index++)
                        {
                            ExportCard card = plan.Cards[index], original = cache.Cards[index];
                            Check(card.Row == r && card.Path == original.Path, "manual group membership and card identity preserved");
                            Check(card.Width == original.Width && card.Height == original.Height, "native 1x card dimensions preserved");
                            if (layout > 0) Check(card.Top == rowTop, "spacing preset never merges or splits a manual row");
                            else Check(card.Left == original.Left && card.Top == original.Top, "current layout keeps exact cropped positions");
                            if (c > 0) Check(card.Left >= plan.Cards[index - 1].Left + plan.Cards[index - 1].Width, "manual column order and non-overlap preserved");
                        }
                    }
                    SaveFixture(cache, settings, "case-manual-rows-" + layout);
                }
                Check(cache.Cards[0].Left == 0 && cache.Cards[3].Top == 101, "spacing changes never mutate the original capture");
            }
            using (RecipeExport cache = new RecipeExport())
            {
                cache.Add(0,0,780,1,Solid(780,1,1),0);
                cache.Add(800,0,780,1,Solid(780,1,2),0);
                cache.Add(0,10,780,1,Solid(780,1,3),1);
                cache.Add(0,20,780,1,Solid(780,1,4),2);
                ExportSettings settings = new ExportSettings(); settings.Layout = ExportLayout.Compact; settings.Margin = ExportMargin.None;
                ExportLayoutPlan plan = cache.CreatePlan(settings);
                Check(plan.Cards[2].Top == 13 && plan.Cards[3].Top == 26, "short singleton manual rows never merge");
            }
        }
        private static void SaveFixture(RecipeExport cache, ExportSettings settings, string name)
        {
            int width, height; Check(cache.Dimensions(settings, out width, out height), "crop/reflow fixture dimensions");
            using (ExportScanlines rows = cache.OpenScanlines(settings))
                PngStreamWriter.Save(Path.Combine(_output, name + ".png"), width, height, rows.Fill);
            int tw, th; byte[] preview = cache.Thumbnail(settings, 2048, 2000000, out tw, out th);
            Check(tw == width && th == height, "crop/reflow fixture preview is full size");
            File.WriteAllBytes(Path.Combine(_output, name + ".rgba"), preview);
        }
        private static void TestPngFailures()
        {
            string path = Path.Combine(_output, "existing.png"); File.WriteAllText(path, "preserve");
            Expect<IOException>(delegate { PngStreamWriter.Save(path, 1, 1, delegate(int y, byte[] row) {}); }, "CreateNew preservation");
            Check(File.ReadAllText(path) == "preserve", "existing export unchanged");
            string failed = Path.Combine(_output, "failed.png");
            Expect<InvalidOperationException>(delegate { PngStreamWriter.Save(failed, 2, 3, delegate(int y, byte[] row) { if (y == 1) throw new InvalidOperationException(); }); }, "encoding failure");
            Check(!File.Exists(failed), "partial PNG cleanup");
            using (MemoryStream stream = new MemoryStream())
            using (PngStreamWriter.Encoder encoder = new PngStreamWriter.Encoder(stream, 1, 2))
            { encoder.WriteRow(new byte[5]); Expect<InvalidOperationException>(encoder.Finish, "incomplete rows rejected"); }
        }
        private static void TestLongPng()
        {
            using (RecipeExport cache = new RecipeExport())
            {
                byte[] tail = new byte[17*2*4];
                for (int i=0;i<tail.Length;i+=4) { tail[i]=255;tail[i+3]=255; }
                cache.Add(0,19998,17,2,tail);
                cache.Add(0,0,1,1,new byte[] {0,255,0,255});
                ExportSettings settings = new ExportSettings(); settings.Background = ExportBackground.Transparent; settings.Margin = ExportMargin.None;
                settings.Layout = ExportLayout.Current;
                using (ExportScanlines source = cache.OpenScanlines(settings))
                    PngStreamWriter.Save(Path.Combine(_output,"long.png"),17,20000,source.Fill);
                int w,h; byte[] small = cache.Thumbnail(settings,2048,2000000,out w,out h);
                Check(w<=2048 && h<=2048 && small.Length<=8000000,"long preview bounded");
                using (ExportThumbnail region = new ExportThumbnail(cache, settings,0,19998,17,2,17,2))
                { while(region.NextRow()){} Check(region.Pixels[0]==255 && region.Pixels[3]==255,"long tail detail complete"); }
            }
            Random random = new Random(902);
            PngStreamWriter.Save(Path.Combine(_output,"noise.png"),513,257,delegate(int y,byte[] row) { random.NextBytes(row); });
        }
        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        private static void TestLayouts()
        {
            string path=Path.Combine(_output,"layouts.txt");
            string key="Campaign|old-level", rows=Encode("old-recipe|0")+","+Encode("old-recipe|1");
            string old="RecipeLayoutV1\nD\t"+Encode(key)+"\t"+rows+"\nS\t"+Encode(key)+"\t"+Encode("Saved 1")+"\t"+rows+"\n";
            File.WriteAllText(path,old,new UTF8Encoding(false));
            RecipeLayoutStore store=new RecipeLayoutStore(key,new ManualLogSource("tests"),path);
            Check(store.ReadDraft()[0][0]=="old-recipe|0" && store.SavedNames()[0]=="Saved 1","v1 layout read");
            store.SaveDraft(store.ReadDraft());
            RecipeLayoutStore reopened=new RecipeLayoutStore(key,new ManualLogSource("tests"),path);
            Check(reopened.ReadSaved("Saved 1")[0][1]=="old-recipe|1","old saved layout preserved");
            Check(File.ReadAllLines(path)[0]=="RecipeLayoutV1","layout header preserved");
        }
        private static void TestConfig()
        {
            string path=Path.Combine(_output,"io.github.overcooked2.recipepreview.cfg");
            File.WriteAllText(path,"[Keyboard shortcuts]\nToggle all recipe images = Insert\n\n[Other mod setting]\nUntouched = keep\n\n[Image export]\nScale = 2\nLayout = Current\nColor = #5078A0\n");
            ConfigFile config=new ConfigFile(path,true);
            ConfigEntry<string> shortcut=config.Bind("Keyboard shortcuts","Toggle all recipe images","bad-default", "");
            ConfigEntry<ExportBackground> background=config.Bind("Image export","Background",ExportBackground.Default, ExportConfiguration.Hidden(""));
            ConfigEntry<ExportMargin> margin=config.Bind("Image export","Margin",ExportMargin.Standard, ExportConfiguration.Hidden(""));
            ConfigEntry<float> scale=config.Bind("Image export","Scale",1f, ExportConfiguration.Hidden(""));
            ConfigEntry<string> color=config.Bind("Image export","Color","#EBE3D1", ExportConfiguration.Hidden(""));
            ConfigEntry<ExportLayout> layout=config.Bind("Image export","Layout",ExportLayout.Standard, ExportConfiguration.Hidden(""));
            ConfigEntry<float> interfaceScale=config.Bind("Interface","Scale",1f,ExportConfiguration.Hidden(""));
            ConfigEntry<UiTheme> theme=config.Bind("Interface","Theme",UiTheme.Kitchen,ExportConfiguration.Hidden(""));
            Check(scale.Value == 2f && color.Value == "#5078A0", "old candidate settings read without loss");
            Check(new ConfigurationManagerAttributes().Browsable == null, "shortcut display remains visible by default");
            ExportSettings settings = new ExportSettings(); settings.Normalize();
            Check(settings.Layout == ExportLayout.Standard && layout.Value == ExportLayout.Current, "new default and legacy current value coexist");
            settings.Layout = layout.Value; settings.Normalize();
            Check(settings.Layout == ExportLayout.Standard, "removed current option migrates to standard without changing enum identities");
            layout.Value = settings.Layout; config.Save();
            ConfigFile migrated = new ConfigFile(path,false);
            Check(migrated.Bind("Image export","Layout",ExportLayout.Standard,"").Value == ExportLayout.Standard,"migrated current config persists as standard");
            foreach (ConfigEntryBase entry in new ConfigEntryBase[] {background, margin, scale, color, layout, interfaceScale, theme})
                Check(entry.Description.Tags.Length == 1 && entry.Description.Tags[0].GetType().Name == "ConfigurationManagerAttributes" &&
                    ((ConfigurationManagerAttributes)entry.Description.Tags[0]).Browsable == false, "export setting hidden from configuration manager");
            background.Value=ExportBackground.Custom; margin.Value=ExportMargin.Wide;scale.Value=1f;layout.Value=ExportLayout.Compact;
            interfaceScale.Value=1.37f;theme.Value=UiTheme.Ocean;config.Save();
            ConfigFile reopened=new ConfigFile(path,false);
            Check(reopened.Bind("Image export","Background",ExportBackground.Default, "").Value==ExportBackground.Custom,"background persistence");
            Check(reopened.Bind("Image export","Margin",ExportMargin.Standard, "").Value==ExportMargin.Wide,"margin persistence");
            Check(reopened.Bind("Image export","Scale",1f, "").Value==1f,"old multiplier reset to fixed 1x");
            Check(reopened.Bind("Image export","Layout",ExportLayout.Current, "").Value==ExportLayout.Compact,"export layout persistence");
            Check(reopened.Bind("Image export","Color","default", "").Value=="#5078A0","color persistence");
            Check(Math.Abs(reopened.Bind("Interface","Scale",1f,"").Value - 1.37f) < 0.0001f,"arbitrary interface percentage persists separately from export scale");
            Check(reopened.Bind("Interface","Theme",UiTheme.Kitchen,"").Value == UiTheme.Ocean,"interface theme persists");
            Check(shortcut.Value=="Insert" && reopened.Bind("Keyboard shortcuts","Toggle all recipe images","bad", "").Value=="Insert","old shortcut preserved");
            Check(reopened.Bind("Other mod setting","Untouched","bad", "").Value=="keep","unknown old settings preserved");
        }

        private static bool Inside(UiBounds outer, UiBounds inner)
        {
            const float epsilon = 0.01f;
            return inner.Width > 0 && inner.Height > 0 && inner.X >= outer.X - epsilon && inner.Y >= outer.Y - epsilon &&
                inner.X + inner.Width <= outer.X + outer.Width + epsilon && inner.Y + inner.Height <= outer.Y + outer.Height + epsilon;
        }
        private static bool Disjoint(UiBounds a, UiBounds b)
        { return a.X + a.Width <= b.X + 0.01f || b.X + b.Width <= a.X + 0.01f || a.Y + a.Height <= b.Y + 0.01f || b.Y + b.Height <= a.Y + 0.01f; }
        private static double Luminance(int rgb)
        {
            double[] channels = {((rgb >> 16) & 255) / 255.0,((rgb >> 8) & 255) / 255.0,(rgb & 255) / 255.0};
            for (int i = 0; i < 3; i++) channels[i] = channels[i] <= 0.04045 ? channels[i] / 12.92 : Math.Pow((channels[i] + 0.055) / 1.055,2.4);
            return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
        }
        private static double Contrast(int a, int b)
        { double x = Luminance(a), y = Luminance(b); return (Math.Max(x,y) + 0.05) / (Math.Min(x,y) + 0.05); }
        private static void TestInterface()
        {
            UiSettings settings = new UiSettings(); settings.Scale = float.NaN; settings.Theme = (UiTheme)99; settings.Normalize();
            Check(settings.Scale == 1 && settings.Theme == UiTheme.Kitchen,"invalid interface preferences recover");
            settings.Scale = float.PositiveInfinity; settings.Normalize(); Check(settings.Scale == 1,"infinite scale recovers");
            settings.Scale = 0.1f; settings.Normalize(); Check(settings.Scale == 0.5f,"minimum interface scale");
            settings.Scale = 8; settings.Normalize(); Check(settings.Scale == 2,"maximum interface scale");
            settings.Scale = 1.37f; settings.Normalize(); Check(settings.Scale == 1.37f,"continuous scale is not a preset");
            int[,] resolutions = {{640,480},{1280,720},{1920,1080},{2560,1440},{3440,1440},{3840,2160},{1080,1920}};
            float[] scales = {0.5f,0.75f,1,1.37f,1.75f,2};
            for (int r = 0; r < resolutions.GetLength(0); r++)
            {
                float previous = 0;
                foreach (float scale in scales)
                {
                    settings.Scale = scale;
                    float effective = settings.ViewScale(resolutions[r,0],resolutions[r,1]);
                    Check(effective > previous,"scale increases monotonically"); previous = effective;
                    float w = resolutions[r,0] / effective, h = resolutions[r,1] / effective;
                    UiBounds screen = new UiBounds(0,0,w,h);
                    UiToolbarLayout toolbar = UiToolbarLayout.Create(w);
                    Check(toolbar.Height + 64 < h,"toolbar preserves usable card viewport");
                    for (int i = 0; i < toolbar.Buttons.Length; i++)
                    {
                        Check(Inside(screen,toolbar.Buttons[i]),"toolbar control stays on screen");
                        for (int j = i + 1; j < toolbar.Buttons.Length; j++) Check(Disjoint(toolbar.Buttons[i],toolbar.Buttons[j]),"toolbar controls never overlap");
                    }
                    UiPreviewLayout preview = UiPreviewLayout.Create(w,h);
                    foreach (UiBounds bounds in new UiBounds[] {preview.Title,preview.Interface,preview.Close,preview.Resolution,preview.Preview,preview.Settings,preview.Status,preview.ZoomLabel})
                        Check(Inside(preview.Window,bounds),"preview control stays inside window");
                    Check(Disjoint(preview.Title,preview.Interface) && Disjoint(preview.Resolution,preview.Interface),"responsive preview header does not overlap");
                    Check(Disjoint(preview.Preview,preview.Settings) && Disjoint(preview.ZoomLabel,preview.Status),"preview, settings and footer remain distinct");
                    foreach (UiBounds button in preview.ZoomButtons)
                        Check(Inside(preview.Window,button) && Disjoint(button,preview.Settings),"zoom controls stay accessible beside sidebar");
                    for (int i = 0; i < preview.Actions.Length; i++)
                    {
                        Check(Inside(preview.Window,preview.Actions[i]),"preview action stays inside window");
                        for (int j = i + 1; j < preview.Actions.Length; j++) Check(Disjoint(preview.Actions[i],preview.Actions[j]),"preview actions never overlap");
                    }
                }
            }
            for (int i = 0; i < 4; i++)
            {
                UiPalette palette = UiPalette.Get((UiTheme)i);
                Check(Contrast(palette.Text,palette.Panel) >= 4.5,"theme panel text contrast");
                Check(Contrast(palette.Text,palette.Button) >= 4.5,"theme button text contrast");
                Check(Contrast(palette.SelectedText,palette.Selected) >= 4.5,"theme selected button contrast");
                Check(Contrast(palette.Muted,palette.Panel) >= 4.5,"theme status text contrast");
                for (int j = i + 1; j < 4; j++) Check(palette.Panel != UiPalette.Get((UiTheme)j).Panel,"four distinct palettes");
            }
            // Interface changes cannot modify export settings or output pixels.
            using (RecipeExport cache = new RecipeExport())
            {
                cache.Add(0,0,2,2,Solid(2,2,42),0);
                ExportSettings export = new ExportSettings(); export.Background = ExportBackground.Transparent; export.Margin = ExportMargin.None;
                int w,h; byte[] before = cache.Thumbnail(export,2048,2000000,out w,out h);
                settings.Scale = 2; settings.Theme = UiTheme.Cream; settings.Normalize();
                byte[] after = cache.Thumbnail(export,2048,2000000,out w,out h);
                Equal(after,before,"interface preferences preserve native export pixels");
                Check(w == 2 && h == 2,"interface preferences preserve native export resolution");
            }
        }
    }
}
