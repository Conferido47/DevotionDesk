using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;

namespace DevotionDesk
{
    public partial class MainWindow : Window
    {
        private const string PdfAssetsHost = "app.local";

        private readonly string _appDataDir;
        private readonly string _devotionalsFolder;
        private readonly string _aliasesPath;
        private readonly string _pdfStatePath;
        private readonly string _settingsPath;

        private readonly string _bibleCacheRootDir;
        private readonly string _bibleSourcesDir;

        private string _currentBibleTranslation = "kjv";
        private bool _suppressBibleTranslationChange;
        private CancellationTokenSource? _bibleDownloadCts;

        private bool _bibleCompareEnabled;
        private string _bibleCompareTranslation = "web";
        private bool _suppressBibleCompareTranslationChange;
        private bool _suppressBibleCompareToggleChange;

        private readonly ObservableCollection<PdfItem> _pdfItems = new();
        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<BibleVerseEntry> _currentChapterVerses = new();
        private readonly List<BibleVerseEntry> _compareChapterVerses = new();
        private readonly ObservableCollection<BibleCompareRow> _bibleCompareRows = new();
        private readonly ObservableCollection<BibleSearchResult> _bibleSearchResults = new();
        private static readonly HttpClient _httpClient = new();
        private readonly JsonSerializerOptions _bibleJsonOptions = new() { PropertyNameCaseInsensitive = true };
        private bool _bibleSelectorsInitialized;
        private bool _suppressBookChange;
        private bool _suppressChapterChange;
        private bool _suppressVerseChange;

        private CancellationTokenSource? _bibleSearchCts;
        private CancellationTokenSource? _bibleNavigateCts;
        private readonly Dictionary<string, List<BibleVerseEntry>> _bibleChapterCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _bibleApiGate = new(1, 1);
        private DateTime _bibleApiLastRequestUtc = DateTime.MinValue;
        private const int BibleApiMinDelayMs = 2100; // bible-api rate limit: ~15 requests / 30s

        private const string BibleOfflineCompleteMarkerFile = ".offline_complete";

        private sealed class BibleTranslationOption
        {
            public string Id { get; init; } = string.Empty; // bible-api translation id
            public string DisplayName { get; init; } = string.Empty;
            public string? OpenBiblesSourceUrl { get; init; }
            public string? OpenBiblesFormat { get; init; } // "osis", "usfx", "zefania"
            public bool CanDownloadOffline => !string.IsNullOrWhiteSpace(OpenBiblesSourceUrl) && !string.IsNullOrWhiteSpace(OpenBiblesFormat);

            public override string ToString() => DisplayName;
        }

        private static readonly List<BibleTranslationOption> _bibleTranslations = new()
        {
            new BibleTranslationOption { Id = "kjv", DisplayName = "KJV (King James)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-kjv.osis.xml", OpenBiblesFormat = "osis" },
            new BibleTranslationOption { Id = "web", DisplayName = "WEB (World English)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-web.usfx.xml", OpenBiblesFormat = "usfx" },
            new BibleTranslationOption { Id = "asv", DisplayName = "ASV (1901)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-asv.zefania.xml", OpenBiblesFormat = "zefania" },
            new BibleTranslationOption { Id = "bbe", DisplayName = "BBE (Basic English)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-bbe.usfx.xml", OpenBiblesFormat = "usfx" },
            new BibleTranslationOption { Id = "darby", DisplayName = "Darby", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-darby.zefania.xml", OpenBiblesFormat = "zefania" },
            new BibleTranslationOption { Id = "dra", DisplayName = "DRA (Douay-Rheims)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-dra.zefania.xml", OpenBiblesFormat = "zefania" },
            new BibleTranslationOption { Id = "ylt", DisplayName = "YLT (Young's Literal)", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-ylt.zefania.xml", OpenBiblesFormat = "zefania" },
            new BibleTranslationOption { Id = "oeb-us", DisplayName = "OEB-US", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-us-oeb.osis.xml", OpenBiblesFormat = "osis" },
            new BibleTranslationOption { Id = "oeb-cw", DisplayName = "OEB-CW", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-gb-oeb.osis.xml", OpenBiblesFormat = "osis" },
            new BibleTranslationOption { Id = "webbe", DisplayName = "WEB British", OpenBiblesSourceUrl = "https://raw.githubusercontent.com/seven1m/open-bibles/master/eng-gb-webbe.usfx.xml", OpenBiblesFormat = "usfx" },
        };

        private const double BibleFontSizeMin = 12.0;
        private const double BibleFontSizeMax = 28.0;
        private double _bibleFontSize = 16.0;

        private sealed class BibleBookOption
        {
            public required string Name { get; init; }
            public required string TestamentName { get; init; } // "Old Testament" / "New Testament"
            public required int TestamentSort { get; init; } // 0=OT, 1=NT
            public required int BookSort { get; init; } // Genesis..Revelation
            public override string ToString() => Name;
        }

        private static readonly HashSet<string> _newTestamentBooks = new(StringComparer.OrdinalIgnoreCase)
        {
            "Matthew",
            "Mark",
            "Luke",
            "John",
            "Acts",
            "Romans",
            "1 Corinthians",
            "2 Corinthians",
            "Galatians",
            "Ephesians",
            "Philippians",
            "Colossians",
            "1 Thessalonians",
            "2 Thessalonians",
            "1 Timothy",
            "2 Timothy",
            "Titus",
            "Philemon",
            "Hebrews",
            "James",
            "1 Peter",
            "2 Peter",
            "1 John",
            "2 John",
            "3 John",
            "Jude",
            "Revelation"
        };

        private static readonly string[] _bibleBookOrder =
        {
            // Old Testament
            "Genesis",
            "Exodus",
            "Leviticus",
            "Numbers",
            "Deuteronomy",
            "Joshua",
            "Judges",
            "Ruth",
            "1 Samuel",
            "2 Samuel",
            "1 Kings",
            "2 Kings",
            "1 Chronicles",
            "2 Chronicles",
            "Ezra",
            "Nehemiah",
            "Esther",
            "Job",
            "Psalms",
            "Proverbs",
            "Ecclesiastes",
            "Song of Solomon",
            "Isaiah",
            "Jeremiah",
            "Lamentations",
            "Ezekiel",
            "Daniel",
            "Hosea",
            "Joel",
            "Amos",
            "Obadiah",
            "Jonah",
            "Micah",
            "Nahum",
            "Habakkuk",
            "Zephaniah",
            "Haggai",
            "Zechariah",
            "Malachi",

            // New Testament
            "Matthew",
            "Mark",
            "Luke",
            "John",
            "Acts",
            "Romans",
            "1 Corinthians",
            "2 Corinthians",
            "Galatians",
            "Ephesians",
            "Philippians",
            "Colossians",
            "1 Thessalonians",
            "2 Thessalonians",
            "1 Timothy",
            "2 Timothy",
            "Titus",
            "Philemon",
            "Hebrews",
            "James",
            "1 Peter",
            "2 Peter",
            "1 John",
            "2 John",
            "3 John",
            "Jude",
            "Revelation"
        };

        private static readonly Dictionary<string, int> _bibleBookOrderIndex =
            _bibleBookOrder
                .Select((name, i) => (name, i))
                .ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _usfxBookIdToName = new(StringComparer.OrdinalIgnoreCase)
        {
            {"GEN","Genesis"}, {"EXO","Exodus"}, {"LEV","Leviticus"}, {"NUM","Numbers"}, {"DEU","Deuteronomy"},
            {"JOS","Joshua"}, {"JDG","Judges"}, {"RUT","Ruth"}, {"1SA","1 Samuel"}, {"2SA","2 Samuel"},
            {"1KI","1 Kings"}, {"2KI","2 Kings"}, {"1CH","1 Chronicles"}, {"2CH","2 Chronicles"}, {"EZR","Ezra"},
            {"NEH","Nehemiah"}, {"EST","Esther"}, {"JOB","Job"}, {"PSA","Psalms"}, {"PRO","Proverbs"},
            {"ECC","Ecclesiastes"}, {"SNG","Song of Solomon"}, {"ISA","Isaiah"}, {"JER","Jeremiah"}, {"LAM","Lamentations"},
            {"EZK","Ezekiel"}, {"DAN","Daniel"}, {"HOS","Hosea"}, {"JOL","Joel"}, {"AMO","Amos"},
            {"OBA","Obadiah"}, {"JON","Jonah"}, {"MIC","Micah"}, {"NAM","Nahum"}, {"HAB","Habakkuk"},
            {"ZEP","Zephaniah"}, {"HAG","Haggai"}, {"ZEC","Zechariah"}, {"MAL","Malachi"},
            {"MAT","Matthew"}, {"MRK","Mark"}, {"LUK","Luke"}, {"JHN","John"}, {"ACT","Acts"}, {"ROM","Romans"},
            {"1CO","1 Corinthians"}, {"2CO","2 Corinthians"}, {"GAL","Galatians"}, {"EPH","Ephesians"}, {"PHP","Philippians"},
            {"COL","Colossians"}, {"1TH","1 Thessalonians"}, {"2TH","2 Thessalonians"}, {"1TI","1 Timothy"}, {"2TI","2 Timothy"},
            {"TIT","Titus"}, {"PHM","Philemon"}, {"HEB","Hebrews"}, {"JAS","James"}, {"1PE","1 Peter"},
            {"2PE","2 Peter"}, {"1JN","1 John"}, {"2JN","2 John"}, {"3JN","3 John"}, {"JUD","Jude"}, {"REV","Revelation"},
        };

        private static readonly Dictionary<string, string> _osisBookIdToName = new(StringComparer.OrdinalIgnoreCase)
        {
            {"Gen","Genesis"}, {"Exod","Exodus"}, {"Lev","Leviticus"}, {"Num","Numbers"}, {"Deut","Deuteronomy"},
            {"Josh","Joshua"}, {"Judg","Judges"}, {"Ruth","Ruth"}, {"1Sam","1 Samuel"}, {"2Sam","2 Samuel"},
            {"1Kgs","1 Kings"}, {"2Kgs","2 Kings"}, {"1Chr","1 Chronicles"}, {"2Chr","2 Chronicles"}, {"Ezra","Ezra"},
            {"Neh","Nehemiah"}, {"Esth","Esther"}, {"Job","Job"}, {"Ps","Psalms"}, {"Prov","Proverbs"},
            {"Eccl","Ecclesiastes"}, {"Song","Song of Solomon"}, {"Isa","Isaiah"}, {"Jer","Jeremiah"}, {"Lam","Lamentations"},
            {"Ezek","Ezekiel"}, {"Dan","Daniel"}, {"Hos","Hosea"}, {"Joel","Joel"}, {"Amos","Amos"},
            {"Obad","Obadiah"}, {"Jonah","Jonah"}, {"Mic","Micah"}, {"Nah","Nahum"}, {"Hab","Habakkuk"},
            {"Zeph","Zephaniah"}, {"Hag","Haggai"}, {"Zech","Zechariah"}, {"Mal","Malachi"},
            {"Matt","Matthew"}, {"Mark","Mark"}, {"Luke","Luke"}, {"John","John"}, {"Acts","Acts"}, {"Rom","Romans"},
            {"1Cor","1 Corinthians"}, {"2Cor","2 Corinthians"}, {"Gal","Galatians"}, {"Eph","Ephesians"}, {"Phil","Philippians"},
            {"Col","Colossians"}, {"1Thess","1 Thessalonians"}, {"2Thess","2 Thessalonians"}, {"1Tim","1 Timothy"}, {"2Tim","2 Timothy"},
            {"Titus","Titus"}, {"Phlm","Philemon"}, {"Heb","Hebrews"}, {"Jas","James"}, {"1Pet","1 Peter"},
            {"2Pet","2 Peter"}, {"1John","1 John"}, {"2John","2 John"}, {"3John","3 John"}, {"Jude","Jude"}, {"Rev","Revelation"},
        };

        private int GetTotalChaptersInCanon()
        {
            var total = 0;
            foreach (var b in _bibleBookOrder)
            {
                if (_bibleChapterCounts.TryGetValue(b, out var c))
                    total += c;
            }
            return total;
        }

        private bool _pdfViewerInitialized;
        private bool _suppressPdfPageChange;
        private bool _restorePdfStatePending;
        private string? _currentPdfFileName;
        private readonly Dictionary<string, PdfReaderState> _pdfReaderStates = new(StringComparer.OrdinalIgnoreCase);

        private AppSettings _settings = new();

        private sealed class BibleCompareRow
        {
            public int VerseNumber { get; init; }
            public string LeftText { get; init; } = string.Empty;
            public string RightText { get; init; } = string.Empty;
        }

        public MainWindow()
        {
            InitializeComponent();

            BibleSearchResults.ItemsSource = _bibleSearchResults;

            if (BibleCompareList != null)
                BibleCompareList.ItemsSource = _bibleCompareRows;

            SetBibleFontSize(_bibleFontSize, syncSlider: true);

            var baseDirDevotionals = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Devotionals");

            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevotionDesk"
            );
            Directory.CreateDirectory(_appDataDir);

            _bibleCacheRootDir = Path.Combine(_appDataDir, "BibleCache");
            Directory.CreateDirectory(_bibleCacheRootDir);

            _bibleSourcesDir = Path.Combine(_appDataDir, "BibleSources");
            Directory.CreateDirectory(_bibleSourcesDir);

            _devotionalsFolder = Path.Combine(_appDataDir, "Devotionals");
            Directory.CreateDirectory(_devotionalsFolder);
            TryMigrateDevotionals(baseDirDevotionals, _devotionalsFolder);

            _aliasesPath = Path.Combine(_devotionalsFolder, "pdf_aliases.json");
            _pdfStatePath = Path.Combine(_appDataDir, "pdf_reader_state.json");
            _settingsPath = Path.Combine(_appDataDir, "settings.json");

            PdfWebView.Visibility = Visibility.Collapsed;
            ReaderStatusText.Visibility = Visibility.Visible;
            ReaderStatusText.Text = "Select a PDF on the left.";

            Loaded += MainWindow_Loaded;
        }

        private void AddPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Add devotionals (PDF)",
                    Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                    Multiselect = true,
                    CheckFileExists = true
                };

                if (dlg.ShowDialog(this) != true)
                    return;

                var addedAny = false;
                foreach (var src in dlg.FileNames ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                        continue;

                    if (!string.Equals(Path.GetExtension(src), ".pdf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dest = Path.Combine(_devotionalsFolder, Path.GetFileName(src));
                    dest = GetAvailablePath(dest);

                    File.Copy(src, dest, overwrite: false);
                    addedAny = true;
                }

                if (!addedAny)
                    return;

                LoadPdfList();
                ApplySort(SortCombo?.SelectedIndex ?? 0);
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show(this, ex.Message, "Add PDFs", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private static string GetAvailablePath(string desiredPath)
        {
            if (!File.Exists(desiredPath))
                return desiredPath;

            var dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(desiredPath);
            var ext = Path.GetExtension(desiredPath);

            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await PdfWebView.EnsureCoreWebView2Async();

                await InitializePdfViewerAsync();

                LoadAliases();
                LoadSettings();
                try { UpdateThemeToggleButtons(); } catch { }

                InitializeBibleTranslations();
                ApplyBibleTranslation(_settings.BibleTranslation, persist: false);

                ApplyBibleCompare(_settings.BibleCompareEnabled, persist: false);

                LoadPdfReaderState();
                LoadPdfList();
                ApplySort(SortCombo?.SelectedIndex ?? 0);

                TrySelectLastOpenedPdf();

                InitializeBibleSelectors();
                await LoadSelectedBibleChapterAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 failed to initialize:\n\n" + ex.Message,
                    "DevotionDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // -------------------------
        // PDFs: list + display names
        // -------------------------
        private void LoadPdfList()
        {
            try
            {
                _pdfItems.Clear();

                var pdfPaths = Directory.GetFiles(_devotionalsFolder, "*.pdf", SearchOption.TopDirectoryOnly);

                foreach (var path in pdfPaths)
                {
                    var fileName = Path.GetFileName(path);
                    var display = MakeDefaultFriendlyName(fileName);

                    if (_aliases.TryGetValue(fileName, out var alias) && !string.IsNullOrWhiteSpace(alias))
                        display = alias;

                    _pdfItems.Add(new PdfItem
                    {
                        FilePath = path,
                        DisplayName = display,
                        LastModified = File.GetLastWriteTime(path)
                    });
                }

                PdfListBox.ItemsSource = _pdfItems;

                ReaderStatusText.Text = _pdfItems.Count == 0
                    ? $"No PDFs found.\n\nPut files into:\n{_devotionalsFolder}"
                    : "Select a PDF on the left.";

                ApplySort(SortCombo?.SelectedIndex ?? 0);
            }
            catch (Exception ex)
            {
                ReaderStatusText.Text = "Error loading PDFs.";
                MessageBox.Show(
                    ex.ToString(),
                    "DevotionDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private static string MakeDefaultFriendlyName(string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            baseName = baseName.Replace("_", " ").Replace("-", " ").Trim();
            return baseName;
        }

        private async void PdfListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PdfListBox.SelectedItem is not PdfItem item)
                return;

            var fullPath = item.FilePath;

            if (!File.Exists(fullPath))
            {
                PdfWebView.Visibility = Visibility.Collapsed;
                ReaderStatusText.Visibility = Visibility.Visible;
                ReaderStatusText.Text = $"Missing file:\n{fullPath}";
                return;
            }

            try
            {
                ReaderStatusText.Visibility = Visibility.Collapsed;
                PdfWebView.Visibility = Visibility.Visible;

                PdfToolbar.Visibility = Visibility.Visible;

                _currentPdfFileName = item.FileName;
                _settings.LastOpenedPdf = _currentPdfFileName;
                SaveSettings();

                _restorePdfStatePending = true;

                await PdfWebView.EnsureCoreWebView2Async();

                OpenPdfInPdfJsViewer(item.FileName);
            }
            catch (Exception ex)
            {
                PdfWebView.Visibility = Visibility.Collapsed;
                PdfToolbar.Visibility = Visibility.Collapsed;
                ReaderStatusText.Visibility = Visibility.Visible;
                ReaderStatusText.Text = "Failed to load PDF.";

                MessageBox.Show(
                    ex.ToString(),
                    "PDF Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // -------------------------
        // Right-click rename (alias)
        // -------------------------
        private void RenamePdf_Click(object sender, RoutedEventArgs e)
        {
            if (PdfListBox.SelectedItem is not PdfItem item) return;

            var dlg = new RenameDialog("Rename PDF (display name)", item.DisplayName)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true)
            {
                var newName = (dlg.Result ?? "").Trim();

                if (string.IsNullOrWhiteSpace(newName))
                {
                    ResetAlias(item);
                    return;
                }

                _aliases[item.FileName] = newName;
                SaveAliases();

                item.DisplayName = newName;
            }
        }

        private void ResetPdfName_Click(object sender, RoutedEventArgs e)
        {
            if (PdfListBox.SelectedItem is not PdfItem item) return;
            ResetAlias(item);
        }

        private void ResetAlias(PdfItem item)
        {
            _aliases.Remove(item.FileName);
            SaveAliases();

            item.DisplayName = MakeDefaultFriendlyName(item.FileName);
        }

        // -------------------------
        // Alias persistence (JSON)
        // -------------------------
        private void LoadAliases()
        {
            _aliases.Clear();

            if (!File.Exists(_aliasesPath))
                return;

            try
            {
                var json = File.ReadAllText(_aliasesPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (dict == null) return;

                foreach (var kv in dict)
                    _aliases[kv.Key] = kv.Value;
            }
            catch
            {
                _aliases.Clear();
            }
        }

        private void SaveAliases()
        {
            try
            {
                var json = JsonSerializer.Serialize(_aliases, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_aliasesPath, json);
            }
            catch
            {
                // ignore
            }
        }

        // -------------------------
        // Sorting
        // -------------------------
        private void PdfListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
                return;

            item.IsSelected = true;
            item.Focus();
        }

        // -------------------------
        // PDFs: pdf.js viewer + controls
        // -------------------------
        private async Task InitializePdfViewerAsync()
        {
            if (_pdfViewerInitialized)
                return;

            if (PdfWebView.CoreWebView2 == null)
                await PdfWebView.EnsureCoreWebView2Async();

            if (PdfWebView.CoreWebView2 == null)
                return;

            EnsurePdfViewerAssetsInAppData();

            // Serve both viewer assets + PDFs from a stable local folder
            PdfWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                PdfAssetsHost,
                _appDataDir,
                CoreWebView2HostResourceAccessKind.Allow
            );

            PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PdfWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

            PdfWebView.CoreWebView2.WebMessageReceived += PdfWebView_WebMessageReceived;

            await PdfWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetPdfJsBridgeScript());

            _pdfViewerInitialized = true;
        }

        private void OpenPdfInPdfJsViewer(string fileName)
        {
            // Keep everything same-origin under https://app.local/
            var pdfUrl = $"https://{PdfAssetsHost}/Devotionals/{Uri.EscapeDataString(fileName)}";
            var viewerUrl = $"https://{PdfAssetsHost}/PdfViewer/pdfjs/web/viewer.html?file={Uri.EscapeDataString(pdfUrl)}";
            PdfWebView.Source = new Uri(viewerUrl, UriKind.Absolute);
        }

        private void EnsurePdfViewerAssetsInAppData()
        {
            try
            {
                var src = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PdfViewer");
                var dst = Path.Combine(_appDataDir, "PdfViewer");

                if (!Directory.Exists(src))
                    return;

                var marker = Path.Combine(dst, "pdfjs", "web", "viewer.html");
                if (File.Exists(marker))
                    return;

                CopyDirectory(src, dst);
            }
            catch
            {
                // ignore
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var destDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
        }

        private static void TryMigrateDevotionals(string oldFolder, string newFolder)
        {
            try
            {
                if (!Directory.Exists(oldFolder))
                    return;

                var newHasAnyPdf = Directory.Exists(newFolder) && Directory.GetFiles(newFolder, "*.pdf").Length > 0;
                if (newHasAnyPdf)
                    return;

                Directory.CreateDirectory(newFolder);

                foreach (var pdf in Directory.GetFiles(oldFolder, "*.pdf", SearchOption.TopDirectoryOnly))
                {
                    var dest = Path.Combine(newFolder, Path.GetFileName(pdf));
                    if (!File.Exists(dest))
                        File.Copy(pdf, dest, overwrite: false);
                }

                var oldAliases = Path.Combine(oldFolder, "pdf_aliases.json");
                var newAliases = Path.Combine(newFolder, "pdf_aliases.json");
                if (File.Exists(oldAliases) && !File.Exists(newAliases))
                    File.Copy(oldAliases, newAliases, overwrite: false);
            }
            catch
            {
                // ignore
            }
        }

        private void LoadSettings()
        {
            _settings = new AppSettings();

            if (!File.Exists(_settingsPath))
                return;

            try
            {
                var json = File.ReadAllText(_settingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                    _settings = s;
            }
            catch
            {
                _settings = new AppSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // ignore
            }
        }

        // -------------------------
        // Theme
        // -------------------------
        private void ApplyThemeAndPersist(string theme)
        {
            _settings.Theme = theme;
            SaveSettings();

            try
            {
                if (Application.Current is App app)
                    app.ApplyTheme(theme);
            }
            catch { }

            // Update any theme-dependent rendered content.
            try { UpdateDisplayedVerses(); } catch { }
            try { UpdateThemeToggleButtons(); } catch { }

            try
            {
                if (PdfWebView?.CoreWebView2 != null && PdfWebView.Visibility == Visibility.Visible)
                    PdfWebView.CoreWebView2.Reload();
            }
            catch { }
        }

        private void UpdateThemeToggleButtons()
        {
            var isDark = string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
            var isLight = !isDark;

            Brush accent;
            Brush onAccent;
            Brush border;
            Brush text;
            try
            {
                accent = (Brush)Application.Current.FindResource("AccentPrimary");
                onAccent = (Brush)Application.Current.FindResource("OnAccent");
                border = (Brush)Application.Current.FindResource("BorderSoft");
                text = (Brush)Application.Current.FindResource("TextMain");
            }
            catch
            {
                return;
            }

            if (ThemeLightButton != null)
            {
                ThemeLightButton.Background = isLight ? accent : Brushes.Transparent;
                ThemeLightButton.BorderBrush = isLight ? accent : border;
                ThemeLightButton.Foreground = isLight ? onAccent : text;
            }

            if (ThemeDarkButton != null)
            {
                ThemeDarkButton.Background = isDark ? accent : Brushes.Transparent;
                ThemeDarkButton.BorderBrush = isDark ? accent : border;
                ThemeDarkButton.Foreground = isDark ? onAccent : text;
            }
        }

        private void ThemeLightButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThemeAndPersist("Light");
        }

        private void ThemeDarkButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThemeAndPersist("Dark");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            if (IsFromCaptionButton(e.OriginalSource as DependencyObject))
                return;

            if (e.ClickCount == 2)
            {
                if (ResizeMode != ResizeMode.NoResize)
                    ToggleMaximizeRestore();
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if the window is not in a draggable state.
            }
        }

        private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsFromCaptionButton(e.OriginalSource as DependencyObject))
                return;

            try
            {
                var p = PointToScreen(e.GetPosition(this));
                SystemCommands.ShowSystemMenu(this, p);
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsFromCaptionButton(DependencyObject? source)
        {
            var d = source;
            while (d != null)
            {
                if (d is Button)
                    return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximizeRestore();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void ToggleMaximizeRestore()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void TrySelectLastOpenedPdf()
        {
            if (string.IsNullOrWhiteSpace(_settings.LastOpenedPdf))
                return;

            var match = _pdfItems.FirstOrDefault(p => string.Equals(p.FileName, _settings.LastOpenedPdf, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                PdfListBox.SelectedItem = match;
        }

        private sealed class AppSettings
        {
            public string? LastOpenedPdf { get; set; }
            public string Theme { get; set; } = "Light"; // "Light" or "Dark"
            public string BibleTranslation { get; set; } = "kjv";
            public bool BibleCompareEnabled { get; set; }
            public string BibleCompareTranslation { get; set; } = "web";
        }

        private string GetPdfJsBridgeScript()
        {
            // Runs on every document; only activates when pdf.js viewer is present.
            return @"(() => {
  const setCssVars = (t) => {
    try {
      const root = document.documentElement;
      if (!root) return;
      if (t && t.bg) root.style.setProperty('--dd-bg', t.bg);
      if (t && t.card) root.style.setProperty('--dd-card', t.card);
      if (t && t.border) root.style.setProperty('--dd-border', t.border);
    } catch { }
  };

  window.__ddApplyTheme = (t) => {
    window.__ddTheme = t || window.__ddTheme || {};
    setCssVars(window.__ddTheme);
  };

  const injectCss = () => {
    const css = `
      :root {
        --dd-bg: rgba(16,17,20,1);
        --dd-card: rgba(23,24,29,0.92);
        --dd-border: rgba(255,255,255,0.15);
      }
      body { background: var(--dd-bg) !important; }
      #sidebarContainer, #secondaryToolbar, #toolbarViewerRight, #toolbarViewerMiddle, #toolbarViewerLeft { display: none !important; }
      #toolbarViewer { display: none !important; }
      #toolbarContainer { box-shadow: none !important; border-bottom: none !important; background: transparent !important; }
      /* Keep findbar working (Ctrl+F) */
      #findbar { background: var(--dd-card) !important; border: 1px solid var(--dd-border) !important; border-radius: 12px !important; }
      /* Remove top offset since toolbar is hidden */
      #viewerContainer { top: 0 !important; }
      #outerContainer.sidebarOpen #viewerContainer { left: 0 !important; }
    `;
    const el = document.createElement('style');
    el.setAttribute('data-dd', 'pdfjs');
    el.textContent = css;
    document.documentElement.appendChild(el);
  };

  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  const waitForPdfJs = async () => {
    for (let i = 0; i < 200; i++) {
      const app = window.PDFViewerApplication;
      if (app && app.initializedPromise) {
        await app.initializedPromise;
        return app;
      }
      await sleep(50);
    }
    return null;
  };

  const postState = (app) => {
    try {
      if (!window.chrome || !chrome.webview) return;
      const pagesCount = app.pagesCount || 0;
      const page = app.page || 1;
      const scale = (app.pdfViewer && app.pdfViewer.currentScale) ? app.pdfViewer.currentScale : 1;
      chrome.webview.postMessage({
        type: 'pdfState',
        page,
        pagesCount,
        scale,
        canPrev: page > 1,
        canNext: pagesCount > 0 ? page < pagesCount : true
      });
    } catch { }
  };

  window.ddApi = {
    async nextPage() {
      const app = await waitForPdfJs();
      if (!app) return;
      const max = app.pagesCount || (app.page || 1);
      app.page = Math.min((app.page || 1) + 1, max);
      postState(app);
    },
    async prevPage() {
      const app = await waitForPdfJs();
      if (!app) return;
      app.page = Math.max((app.page || 1) - 1, 1);
      postState(app);
    },
    async setPage(n) {
      const app = await waitForPdfJs();
      if (!app) return;
      const num = Number(n);
      if (!Number.isFinite(num)) return;
      const max = app.pagesCount || num;
      app.page = Math.min(Math.max(Math.trunc(num), 1), max);
      postState(app);
    },
    async zoomIn() {
      const app = await waitForPdfJs();
      if (!app) return;
      app.zoomIn();
      postState(app);
    },
    async zoomOut() {
      const app = await waitForPdfJs();
      if (!app) return;
      app.zoomOut();
      postState(app);
    },
    async fitWidth() {
      const app = await waitForPdfJs();
      if (!app || !app.pdfViewer) return;
      app.pdfViewer.currentScaleValue = 'page-width';
      postState(app);
    },
    async setScale(scale) {
      const app = await waitForPdfJs();
      if (!app || !app.pdfViewer) return;
      const s = Number(scale);
      if (!Number.isFinite(s) || s <= 0) return;
      app.pdfViewer.currentScale = s;
      postState(app);
    },
    async getState() {
      const app = await waitForPdfJs();
      if (!app) return null;
      const pagesCount = app.pagesCount || 0;
      const page = app.page || 1;
      const scale = (app.pdfViewer && app.pdfViewer.currentScale) ? app.pdfViewer.currentScale : 1;
      return { page, pagesCount, scale };
    },
    async findAndJump(query) {
      const app = await waitForPdfJs();
      if (!app || !app.eventBus || !app.findController) return false;

      const q = (query || '').toString().trim();
      if (!q) return false;

      // Kick off find; pdf.js will update the selected match and scroll.
      app.eventBus.dispatch('find', {
        query: q,
        phraseSearch: true,
        caseSensitive: false,
        highlightAll: true,
        findPrevious: false
      });

      // Give the controller a moment to select the first match.
      await sleep(250);

      try {
        const sel = app.findController._selected;
        if (sel && typeof sel.pageIdx === 'number') {
          app.page = sel.pageIdx + 1;
          postState(app);
          return true;
        }
      } catch { }

      postState(app);
      return false;
    }
  };

  (async () => {
    injectCss();
    try { window.__ddApplyTheme(window.__ddTheme || {}); } catch { }
    const app = await waitForPdfJs();
    if (!app || !app.eventBus) return;

    const onAny = () => postState(app);
    app.eventBus.on('pagesloaded', onAny);
    app.eventBus.on('pagechanging', onAny);
    app.eventBus.on('scalechanging', onAny);
    app.eventBus.on('documentloaded', onAny);
    postState(app);
  })();
})();";
        }

        private static Color? TryGetBrushColor(string key)
        {
            try
            {
                if (Application.Current == null) return null;
                if (!Application.Current.Resources.Contains(key)) return null;
                if (Application.Current.Resources[key] is not SolidColorBrush b) return null;
                return b.Color;
            }
            catch
            {
                return null;
            }
        }

        private static string CssRgba(Color c)
        {
            var a = Math.Round(c.A / 255.0, 3);
            return $"rgba({c.R},{c.G},{c.B},{a})";
        }

        private string GetPdfThemeJsonForWeb()
        {
            var bg = CssRgba(TryGetBrushColor("BgApp") ?? Color.FromRgb(16, 17, 20));
            var card = CssRgba(TryGetBrushColor("BgCard") ?? Color.FromRgb(23, 24, 29));
            var border = CssRgba(TryGetBrushColor("BorderSoft") ?? Color.FromArgb(60, 255, 255, 255));

            return JsonSerializer.Serialize(new { bg, card, border });
        }

        private async Task ApplyPdfThemeToWebViewAsync()
        {
            try
            {
                if (PdfWebView?.CoreWebView2 == null)
                    return;

                var json = GetPdfThemeJsonForWeb();
                await PdfWebView.CoreWebView2.ExecuteScriptAsync($"window.__ddApplyTheme && window.__ddApplyTheme({json})");
            }
            catch
            {
                // ignore
            }
        }

        private void PdfWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl))
                    return;

                if (!string.Equals(typeEl.GetString(), "pdfState", StringComparison.OrdinalIgnoreCase))
                    return;

                var page = root.TryGetProperty("page", out var pageEl) ? pageEl.GetInt32() : 1;
                var pagesCount = root.TryGetProperty("pagesCount", out var countEl) ? countEl.GetInt32() : 0;
                var scale = root.TryGetProperty("scale", out var scaleEl) ? scaleEl.GetDouble() : 1.0;
                var canPrev = root.TryGetProperty("canPrev", out var canPrevEl) && canPrevEl.GetBoolean();
                var canNext = root.TryGetProperty("canNext", out var canNextEl) && canNextEl.GetBoolean();

                Dispatcher.Invoke(() =>
                {
                    UpdatePdfToolbarState(page, pagesCount, scale, canPrev, canNext);

                    if (_restorePdfStatePending)
                        TryRestorePdfState(page, pagesCount);

                    SavePdfReaderStateFromViewer(page, scale);
                });
            }
            catch
            {
                // ignore
            }
        }

        private void PdfWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // Best-effort: request state refresh once navigation completes.
            _ = ApplyPdfThemeToWebViewAsync();
            _ = PdfExecuteAsync("window.ddApi && window.ddApi.getState && window.ddApi.getState()")
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }

        private void UpdatePdfToolbarState(int page, int pagesCount, double scale, bool canPrev, bool canNext)
        {
            PdfPrevButton.IsEnabled = canPrev;
            PdfNextButton.IsEnabled = canNext;

            PdfPageCountText.Text = $"/ {pagesCount}";

            _suppressPdfPageChange = true;
            PdfPageTextBox.Text = page.ToString();
            _suppressPdfPageChange = false;

            var pct = (int)Math.Round(scale * 100);
            PdfZoomText.Text = $"{pct}%";
        }

        private async Task PdfExecuteAsync(string js)
        {
            try
            {
                if (PdfWebView.CoreWebView2 == null)
                    return;

                await PdfWebView.ExecuteScriptAsync(js);
            }
            catch
            {
                // ignore
            }
        }

        private async Task<string?> PdfEvalAsync(string js)
        {
            try
            {
                if (PdfWebView.CoreWebView2 == null)
                    return null;

                return await PdfWebView.ExecuteScriptAsync(js);
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> PdfEvalBoolAsync(string js)
        {
            var result = await PdfEvalAsync(js);
            if (string.IsNullOrWhiteSpace(result))
                return false;

            // WebView2 returns JSON encoded values.
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(result, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            if (bool.TryParse(result.Trim('"'), out var b))
                return b;

            return false;
        }

        private async void PdfPrevButton_Click(object sender, RoutedEventArgs e)
            => await PdfExecuteAsync("window.ddApi && window.ddApi.prevPage && window.ddApi.prevPage()");

        private async void PdfNextButton_Click(object sender, RoutedEventArgs e)
            => await PdfExecuteAsync("window.ddApi && window.ddApi.nextPage && window.ddApi.nextPage()");

        private async void PdfZoomOutButton_Click(object sender, RoutedEventArgs e)
            => await PdfExecuteAsync("window.ddApi && window.ddApi.zoomOut && window.ddApi.zoomOut()");

        private async void PdfZoomInButton_Click(object sender, RoutedEventArgs e)
            => await PdfExecuteAsync("window.ddApi && window.ddApi.zoomIn && window.ddApi.zoomIn()");

        private async void PdfFitWidthButton_Click(object sender, RoutedEventArgs e)
            => await PdfExecuteAsync("window.ddApi && window.ddApi.fitWidth && window.ddApi.fitWidth()");

        private void PdfPageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            _ = TrySetPdfPageFromTextBoxAsync();
        }

        private void PdfPageTextBox_LostFocus(object sender, RoutedEventArgs e)
            => _ = TrySetPdfPageFromTextBoxAsync();

        private async Task TrySetPdfPageFromTextBoxAsync()
        {
            if (_suppressPdfPageChange)
                return;

            var raw = (PdfPageTextBox.Text ?? string.Empty).Trim();
            if (!int.TryParse(raw, out var page) || page < 1)
                return;

            await PdfExecuteAsync($"window.ddApi && window.ddApi.setPage && window.ddApi.setPage({page})");
        }

        private void LoadPdfReaderState()
        {
            _pdfReaderStates.Clear();

            if (!File.Exists(_pdfStatePath))
                return;

            try
            {
                var json = File.ReadAllText(_pdfStatePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, PdfReaderState>>(json);
                if (dict == null)
                    return;

                foreach (var kv in dict)
                    _pdfReaderStates[kv.Key] = kv.Value;
            }
            catch
            {
                _pdfReaderStates.Clear();
            }
        }

        private void SavePdfReaderStateFromViewer(int page, double scale)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfFileName))
                return;

            _pdfReaderStates[_currentPdfFileName] = new PdfReaderState
            {
                Page = page,
                Scale = scale
            };

            try
            {
                var json = JsonSerializer.Serialize(_pdfReaderStates, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_pdfStatePath, json);
            }
            catch
            {
                // ignore
            }
        }

        private void TryRestorePdfState(int currentPage, int pagesCount)
        {
            if (!_restorePdfStatePending)
                return;

            if (string.IsNullOrWhiteSpace(_currentPdfFileName))
                return;

            if (pagesCount <= 0)
                return;

            if (!_pdfReaderStates.TryGetValue(_currentPdfFileName, out var state))
            {
                _restorePdfStatePending = false;
                return;
            }

            _restorePdfStatePending = false;

            if (state.Page > 1 && state.Page != currentPage)
                _ = PdfExecuteAsync($"window.ddApi && window.ddApi.setPage && window.ddApi.setPage({state.Page})");

            if (state.Scale > 0.1 && Math.Abs(state.Scale - 1.0) > 0.01)
                _ = PdfExecuteAsync($"window.ddApi && window.ddApi.setScale && window.ddApi.setScale({state.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }

        private sealed class PdfReaderState
        {
            public int Page { get; set; } = 1;
            public double Scale { get; set; } = 1.0;
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (PdfListBox?.ItemsSource == null) return;

            ApplySort(SortCombo.SelectedIndex);
        }

        private void ApplySort(int mode)
        {
            if (PdfListBox == null) return;
            if (PdfListBox.ItemsSource == null) return;

            var view = CollectionViewSource.GetDefaultView(PdfListBox.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();

            switch (mode)
            {
                case 0: // A → Z
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PdfItem.DisplayName), System.ComponentModel.ListSortDirection.Ascending));
                    break;
                case 1: // Z → A
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PdfItem.DisplayName), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case 2: // Newest first
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PdfItem.LastModified), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case 3: // Oldest first
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PdfItem.LastModified), System.ComponentModel.ListSortDirection.Ascending));
                    break;
                default:
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PdfItem.DisplayName), System.ComponentModel.ListSortDirection.Ascending));
                    break;
            }

            view.Refresh();
        }

        private void InitializeBibleSelectors()
        {
            if (_bibleSelectorsInitialized) return;
            _bibleSelectorsInitialized = true;

            var options = _bibleChapterCounts.Keys
                .Select(name => new BibleBookOption
                {
                    Name = name,
                    TestamentName = _newTestamentBooks.Contains(name) ? "New Testament" : "Old Testament",
                    TestamentSort = _newTestamentBooks.Contains(name) ? 1 : 0,
                    BookSort = _bibleBookOrderIndex.TryGetValue(name, out var idx) ? idx : int.MaxValue
                })
                .ToList();

            var view = CollectionViewSource.GetDefaultView(options);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BibleBookOption.TestamentSort), System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(BibleBookOption.BookSort), System.ComponentModel.ListSortDirection.Ascending));
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BibleBookOption.TestamentName)));

            BookCombo.ItemsSource = view;
            BookCombo.SelectedItem = options.FirstOrDefault(o => string.Equals(o.Name, "Genesis", StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        }

        private void InitializeBibleTranslations()
        {
            if (BibleTranslationCombo == null)
                return;

            _suppressBibleTranslationChange = true;
            BibleTranslationCombo.ItemsSource = _bibleTranslations;

            var desired = string.IsNullOrWhiteSpace(_settings.BibleTranslation) ? "kjv" : _settings.BibleTranslation;
            var exists = _bibleTranslations.Any(t => string.Equals(t.Id, desired, StringComparison.OrdinalIgnoreCase));
            BibleTranslationCombo.SelectedValue = exists ? desired : "kjv";

            _suppressBibleTranslationChange = false;

            if (BibleCompareTranslationCombo != null)
            {
                _suppressBibleCompareTranslationChange = true;
                BibleCompareTranslationCombo.ItemsSource = _bibleTranslations;

                var compareDesired = string.IsNullOrWhiteSpace(_settings.BibleCompareTranslation) ? "web" : _settings.BibleCompareTranslation;
                var compareExists = _bibleTranslations.Any(t => string.Equals(t.Id, compareDesired, StringComparison.OrdinalIgnoreCase));
                BibleCompareTranslationCombo.SelectedValue = compareExists ? compareDesired : "web";
                _suppressBibleCompareTranslationChange = false;
            }

            UpdateBibleDownloadButtonState();

            if (_bibleCompareEnabled)
                EnsureCompareTranslationNotSame(persistIfChanged: true);
        }

        private void ApplyBibleTranslation(string? translationId, bool persist)
        {
            var id = string.IsNullOrWhiteSpace(translationId) ? "kjv" : translationId.Trim();
            var opt = _bibleTranslations.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            if (opt == null)
            {
                id = "kjv";
                opt = _bibleTranslations.First(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            }

            _currentBibleTranslation = opt.Id;

            if (persist)
            {
                _settings.BibleTranslation = opt.Id;
                SaveSettings();
            }

            _bibleChapterCache.Clear();

            try
            {
                _bibleSearchResults.Clear();
                BibleSearchResults.Visibility = Visibility.Collapsed;
                BibleSearchStatus.Text = "Enter a keyword to search across scripture.";
            }
            catch { }

            UpdateBibleDownloadButtonState();
        }

        private void ApplyBibleCompare(bool enabled, bool persist)
        {
            _bibleCompareEnabled = enabled;

            if (persist)
            {
                _settings.BibleCompareEnabled = enabled;
                SaveSettings();
            }

            if (BibleCompareToggle != null)
            {
                _suppressBibleCompareToggleChange = true;
                BibleCompareToggle.IsChecked = enabled;
                _suppressBibleCompareToggleChange = false;
            }

            if (BibleCompareTranslationCombo != null)
                BibleCompareTranslationCombo.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            if (enabled)
            {
                var selected = BibleCompareTranslationCombo?.SelectedValue as string;
                _bibleCompareTranslation = string.IsNullOrWhiteSpace(selected)
                    ? (string.IsNullOrWhiteSpace(_settings.BibleCompareTranslation) ? "web" : _settings.BibleCompareTranslation)
                    : selected;

                EnsureCompareTranslationNotSame(persistIfChanged: persist);
            }
            else
            {
                _compareChapterVerses.Clear();
                _bibleCompareRows.Clear();

                if (BibleComparePanel != null)
                    BibleComparePanel.Visibility = Visibility.Collapsed;
            }

            try { UpdateDisplayedVerses(); } catch { }
        }

        private void BibleCompareToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressBibleCompareToggleChange)
                return;

            ApplyBibleCompare(true, persist: true);
            _ = LoadSelectedBibleChapterAsync();
        }

        private void BibleCompareToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressBibleCompareToggleChange)
                return;

            ApplyBibleCompare(false, persist: true);
        }

        private void BibleCompareTranslationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressBibleCompareTranslationChange)
                return;

            if (BibleCompareTranslationCombo?.SelectedValue is not string id)
                return;

            _bibleCompareTranslation = id;

            _settings.BibleCompareTranslation = id;
            SaveSettings();

            EnsureCompareTranslationNotSame(persistIfChanged: true);

            if (_bibleCompareEnabled)
                _ = LoadSelectedBibleChapterAsync();
        }

        private bool EnsureCompareTranslationNotSame(bool persistIfChanged)
        {
            var primary = _currentBibleTranslation;
            var compare = _bibleCompareTranslation;

            if (!string.Equals(primary, compare, StringComparison.OrdinalIgnoreCase))
                return false;

            var fallback = string.Equals(primary, "web", StringComparison.OrdinalIgnoreCase) ? "kjv" : "web";
            if (!_bibleTranslations.Any(t => string.Equals(t.Id, fallback, StringComparison.OrdinalIgnoreCase)))
                fallback = _bibleTranslations.FirstOrDefault(t => !string.Equals(t.Id, primary, StringComparison.OrdinalIgnoreCase))?.Id ?? primary;

            _bibleCompareTranslation = fallback;

            if (BibleCompareTranslationCombo != null)
            {
                _suppressBibleCompareTranslationChange = true;
                BibleCompareTranslationCombo.SelectedValue = fallback;
                _suppressBibleCompareTranslationChange = false;
            }

            if (persistIfChanged)
            {
                _settings.BibleCompareTranslation = fallback;
                SaveSettings();
            }

            return true;
        }

        private void UpdateBibleDownloadButtonState()
        {
            try
            {
                var opt = GetSelectedBibleTranslationOption();
                if (BibleDownloadButton != null)
                {
                    BibleDownloadButton.IsEnabled = opt?.CanDownloadOffline == true && _bibleDownloadCts == null;
                    if (opt?.CanDownloadOffline != true)
                        BibleDownloadButton.Content = "Offline unavailable";
                    else
                        BibleDownloadButton.Content = IsTranslationOfflineReady(_currentBibleTranslation) ? "Offline ready" : "Download offline";
                }
            }
            catch { }
        }

        private BibleTranslationOption? GetSelectedBibleTranslationOption()
        {
            try
            {
                var id = BibleTranslationCombo?.SelectedValue as string;
                if (string.IsNullOrWhiteSpace(id))
                    id = _currentBibleTranslation;
                return _bibleTranslations.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private void BibleTranslationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressBibleTranslationChange)
                return;

            if (BibleTranslationCombo?.SelectedValue is not string id)
                return;

            ApplyBibleTranslation(id, persist: true);

            try
            {
                _ = LoadSelectedBibleChapterAsync();
            }
            catch { }
        }

        private async void BibleDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var opt = GetSelectedBibleTranslationOption();
            if (opt?.CanDownloadOffline != true)
            {
                MessageBox.Show(
                    "Offline download isn't available for this version in the current build.\n\n" +
                    "Tip: KJV/WEB/ASV/BBE/Darby/DRA/YLT/OEB/WEB British are supported.",
                    "DevotionDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (IsTranslationOfflineReady(opt.Id))
            {
                MessageBox.Show(
                    "This Bible version is already downloaded for offline use.",
                    "DevotionDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (_bibleDownloadCts != null)
                return;

            _bibleDownloadCts = new CancellationTokenSource();
            var token = _bibleDownloadCts.Token;

            try
            {
                ShowBibleDownloadUi(opt);
                UpdateBibleDownloadButtonState();

                await DownloadAndImportOfflineBibleAsync(opt, token);

                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        BibleDownloadStatus.Text = "Offline Bible ready.";
                        BibleDownloadProgress.Value = BibleDownloadProgress.Maximum;
                    }
                    catch { }

                    UpdateBibleDownloadButtonState();
                }
            }
            catch (OperationCanceledException)
            {
                try { BibleDownloadStatus.Text = "Download cancelled."; } catch { }
            }
            catch (Exception ex)
            {
                try { BibleDownloadStatus.Text = "Offline download failed: " + ex.Message; } catch { }
            }
            finally
            {
                _bibleDownloadCts?.Dispose();
                _bibleDownloadCts = null;
                UpdateBibleDownloadButtonState();

                // Hide after a moment (keeps the UI from flashing).
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(1200);
                        BibleDownloadPanel.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                });
            }
        }

        private void BibleDownloadCancelButton_Click(object sender, RoutedEventArgs e)
        {
            try { _bibleDownloadCts?.Cancel(); } catch { }
        }

        private void ShowBibleDownloadUi(BibleTranslationOption opt)
        {
            if (BibleDownloadPanel == null || BibleDownloadStatus == null || BibleDownloadProgress == null)
                return;

            BibleDownloadPanel.Visibility = Visibility.Visible;
            BibleDownloadStatus.Text = $"Preparing {opt.DisplayName} for offline use…";
            BibleDownloadProgress.IsIndeterminate = true;
            BibleDownloadProgress.Minimum = 0;
            BibleDownloadProgress.Maximum = Math.Max(1, GetTotalChaptersInCanon());
            BibleDownloadProgress.Value = 0;
        }

        private async Task DownloadAndImportOfflineBibleAsync(BibleTranslationOption opt, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(opt.OpenBiblesSourceUrl) || string.IsNullOrWhiteSpace(opt.OpenBiblesFormat))
                throw new InvalidOperationException("No offline source is configured for this translation.");

            var uri = new Uri(opt.OpenBiblesSourceUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{opt.Id}.xml";

            var sourcePath = Path.Combine(_bibleSourcesDir, fileName);

            // Download source XML (GitHub raw).
            await Dispatcher.InvokeAsync(() =>
            {
                try { BibleDownloadStatus.Text = $"Downloading source ({opt.Id})…"; } catch { }
            });

            await DownloadFileAsync(uri, sourcePath, token);

            var totalChapters = GetTotalChaptersInCanon();
            var completed = 0;

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    BibleDownloadStatus.Text = "Importing into offline cache…";
                    BibleDownloadProgress.IsIndeterminate = false;
                    BibleDownloadProgress.Minimum = 0;
                    BibleDownloadProgress.Maximum = Math.Max(1, totalChapters);
                    BibleDownloadProgress.Value = 0;
                }
                catch { }
            });

            if (string.Equals(opt.OpenBiblesFormat, "zefania", StringComparison.OrdinalIgnoreCase))
            {
                completed = await ImportZefaniaXmlAsync(opt.Id, sourcePath, totalChapters, token);
            }
            else if (string.Equals(opt.OpenBiblesFormat, "usfx", StringComparison.OrdinalIgnoreCase))
            {
                completed = await ImportUsfxXmlAsync(opt.Id, sourcePath, totalChapters, token);
            }
            else if (string.Equals(opt.OpenBiblesFormat, "osis", StringComparison.OrdinalIgnoreCase))
            {
                completed = await ImportOsisXmlAsync(opt.Id, sourcePath, totalChapters, token);
            }
            else
            {
                throw new InvalidOperationException("Unknown offline source format: " + opt.OpenBiblesFormat);
            }

            token.ThrowIfCancellationRequested();

            // Mark complete.
            var marker = GetOfflineCompleteMarkerPath(opt.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    BibleDownloadStatus.Text = $"Offline cache built ({completed}/{totalChapters} chapters).";
                    BibleDownloadProgress.Value = Math.Min(totalChapters, completed);
                }
                catch { }
            });
        }

        private static async Task DownloadFileAsync(Uri uri, string path, CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();

            await using var input = await resp.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await input.CopyToAsync(output, 1024 * 128, token);
        }

        private void BookCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_bibleSelectorsInitialized) return;
            if (_suppressBookChange) return;
            if (BookCombo?.SelectedItem is not BibleBookOption bookOpt) return;
            if (!_bibleChapterCounts.TryGetValue(bookOpt.Name, out var chapterCount)) return;

            _suppressChapterChange = true;
            ChapterCombo.ItemsSource = Enumerable.Range(1, chapterCount).ToList();
            ChapterCombo.SelectedIndex = 0;
            _suppressChapterChange = false;

            _ = LoadSelectedBibleChapterAsync();
        }

        private void ChapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_bibleSelectorsInitialized || _suppressChapterChange) return;
            _ = LoadSelectedBibleChapterAsync();
        }

        private void VerseStartCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_bibleSelectorsInitialized || _suppressVerseChange) return;
            EnsureVerseRangeOrder(adjustEnd: true);
            UpdateDisplayedVerses();
        }

        private void VerseEndCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_bibleSelectorsInitialized || _suppressVerseChange) return;
            EnsureVerseRangeOrder(adjustEnd: false);
            UpdateDisplayedVerses();
        }

        private void RefreshBibleButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadSelectedBibleChapterAsync(true);
        }

        private async Task LoadSelectedBibleChapterAsync(bool explicitRefresh = false)
        {
            if (BookCombo?.SelectedItem is not BibleBookOption bookOpt) return;
            if (ChapterCombo?.SelectedItem is not int chapter) return;

            var reference = $"{bookOpt.Name} {chapter}";
            BibleStatusText.Text = $"Loading {reference}…";
            BibleStatusText.Visibility = Visibility.Visible;
            BibleReader.Visibility = Visibility.Collapsed;

            try
            {
                var primaryVerses = await FetchChapterVersesAsync(_currentBibleTranslation, bookOpt.Name, chapter, CancellationToken.None);

                List<BibleVerseEntry> compareVerses = new();
                if (_bibleCompareEnabled)
                {
                    EnsureCompareTranslationNotSame(persistIfChanged: true);
                    compareVerses = await FetchChapterVersesAsync(_bibleCompareTranslation, bookOpt.Name, chapter, CancellationToken.None);
                }

                if (primaryVerses.Count == 0 && (!_bibleCompareEnabled || compareVerses.Count == 0))
                {
                    BibleStatusText.Text = "No verses returned for this reference.";
                    return;
                }

                _currentChapterVerses.Clear();
                _currentChapterVerses.AddRange(primaryVerses.OrderBy(v => v.VerseNumber));

                _compareChapterVerses.Clear();
                _compareChapterVerses.AddRange(compareVerses.OrderBy(v => v.VerseNumber));

                var verseNumbers = primaryVerses.Select(v => v.VerseNumber)
                    .Concat(compareVerses.Select(v => v.VerseNumber))
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                _suppressVerseChange = true;
                VerseStartCombo.ItemsSource = verseNumbers;
                VerseEndCombo.ItemsSource = verseNumbers;
                if (verseNumbers.Count > 0)
                {
                    VerseStartCombo.SelectedItem = verseNumbers.First();
                    VerseEndCombo.SelectedItem = verseNumbers.First();
                }
                _suppressVerseChange = false;

                UpdateDisplayedVerses();
            }
            catch (Exception ex)
            {
                BibleStatusText.Text = "Bible service error: " + ex.Message;
                BibleStatusText.Visibility = Visibility.Visible;
                BibleReader.Visibility = Visibility.Collapsed;
            }
        }

        private void EnsureVerseRangeOrder(bool adjustEnd)
        {
            if (VerseStartCombo?.SelectedItem is not int start || VerseEndCombo?.SelectedItem is not int end)
                return;

            if (adjustEnd && end < start)
            {
                _suppressVerseChange = true;
                VerseEndCombo.SelectedItem = start;
                _suppressVerseChange = false;
            }
            else if (!adjustEnd && end < start)
            {
                _suppressVerseChange = true;
                VerseStartCombo.SelectedItem = end;
                _suppressVerseChange = false;
            }
        }

        private void UpdateDisplayedVerses()
        {
            _bibleCompareRows.Clear();

            if (VerseStartCombo?.SelectedItem is not int start || VerseEndCombo?.SelectedItem is not int end)
            {
                BibleStatusText.Text = "Select a book, chapter, and verse.";
                BibleStatusText.Visibility = Visibility.Visible;
                BibleReader.Visibility = Visibility.Collapsed;
                if (BibleComparePanel != null) BibleComparePanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (end < start)
            {
                EnsureVerseRangeOrder(adjustEnd: false);
                return;
            }

            BibleStatusText.Visibility = Visibility.Collapsed;

            if (BookCombo?.SelectedItem is not BibleBookOption bookOpt || ChapterCombo?.SelectedItem is not int chapter)
                return;

            if (_bibleCompareEnabled)
            {
                var leftOpt = GetSelectedBibleTranslationOption();
                var rightOpt = _bibleTranslations.FirstOrDefault(t => string.Equals(t.Id, _bibleCompareTranslation, StringComparison.OrdinalIgnoreCase));

                if (BibleCompareLeftHeader != null)
                    BibleCompareLeftHeader.Text = leftOpt?.DisplayName ?? _currentBibleTranslation;
                if (BibleCompareRightHeader != null)
                    BibleCompareRightHeader.Text = rightOpt?.DisplayName ?? _bibleCompareTranslation;

                var leftMap = _currentChapterVerses
                    .GroupBy(v => v.VerseNumber)
                    .ToDictionary(g => g.Key, g => (g.LastOrDefault()?.Text ?? string.Empty).Trim());

                var rightMap = _compareChapterVerses
                    .GroupBy(v => v.VerseNumber)
                    .ToDictionary(g => g.Key, g => (g.LastOrDefault()?.Text ?? string.Empty).Trim());

                var verseNums = leftMap.Keys
                    .Concat(rightMap.Keys)
                    .Distinct()
                    .Where(v => v >= start && v <= end)
                    .OrderBy(v => v)
                    .ToList();

                foreach (var vnum in verseNums)
                {
                    leftMap.TryGetValue(vnum, out var leftText);
                    rightMap.TryGetValue(vnum, out var rightText);

                    leftText = (leftText ?? string.Empty).Replace('\n', ' ').Trim();
                    rightText = (rightText ?? string.Empty).Replace('\n', ' ').Trim();

                    if (leftText.Length == 0 && rightText.Length == 0)
                        continue;

                    _bibleCompareRows.Add(new BibleCompareRow
                    {
                        VerseNumber = vnum,
                        LeftText = leftText,
                        RightText = rightText
                    });
                }

                if (_bibleCompareRows.Count == 0)
                {
                    BibleStatusText.Text = "No verses loaded for this range.";
                    BibleStatusText.Visibility = Visibility.Visible;
                    BibleReader.Visibility = Visibility.Collapsed;
                    if (BibleComparePanel != null) BibleComparePanel.Visibility = Visibility.Collapsed;
                    return;
                }

                BibleReader.Visibility = Visibility.Collapsed;
                if (BibleComparePanel != null) BibleComparePanel.Visibility = Visibility.Visible;

                BibleReader.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (BibleCompareList != null && BibleCompareList.Items.Count > 0)
                            BibleCompareList.ScrollIntoView(BibleCompareList.Items[0]);
                    }
                    catch { }
                }));

                return;
            }

            var range = _currentChapterVerses
                .Where(v => v.VerseNumber >= start && v.VerseNumber <= end)
                .OrderBy(v => v.VerseNumber)
                .ToList();

            if (range.Count == 0)
            {
                BibleStatusText.Text = "No verses loaded for this range.";
                BibleStatusText.Visibility = Visibility.Visible;
                BibleReader.Visibility = Visibility.Collapsed;
                if (BibleComparePanel != null) BibleComparePanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (BibleComparePanel != null)
                BibleComparePanel.Visibility = Visibility.Collapsed;

            BibleReader.Document = BuildBibleFlowDocument($"{bookOpt.Name} {chapter}", range);
            BibleReader.Visibility = Visibility.Visible;

            BibleReader.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var sv = FindDescendantScrollViewer(BibleReader);
                    sv?.ScrollToTop();
                }
                catch { }
            }));
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;

                var nested = FindDescendantScrollViewer(child);
                if (nested != null) return nested;
            }

            return null;
        }

        private FlowDocument BuildBibleFlowDocument(string reference, IReadOnlyList<BibleVerseEntry> verses)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(14, 12, 24, 16),
                ColumnWidth = 99999
            };

            var header = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 10),
                FontFamily = (FontFamily)FindResource("FontUIBold"),
                FontSize = Math.Max(BibleFontSizeMin, _bibleFontSize + 2),
                Foreground = (Brush)FindResource("TextMain")
            };
            header.Inlines.Add(new Run(reference));
            doc.Blocks.Add(header);

            var body = new Paragraph
            {
                Margin = new Thickness(0),
                FontFamily = (FontFamily)FindResource("FontSubText"),
                FontSize = _bibleFontSize,
                Foreground = (Brush)FindResource("TextMain"),
                LineHeight = Math.Round(_bibleFontSize * 1.55, 2)
            };

            var numBrush = (Brush)FindResource("TextMuted");
            var numFont = (FontFamily)FindResource("FontUIBold");

            foreach (var verse in verses)
            {
                var num = new Run($"{verse.VerseNumber}")
                {
                    BaselineAlignment = BaselineAlignment.Superscript,
                    FontFamily = numFont,
                    FontSize = Math.Max(BibleFontSizeMin, _bibleFontSize * 0.75),
                    Foreground = numBrush
                };
                body.Inlines.Add(num);
                body.Inlines.Add(new Run(" "));

                var text = (verse.Text ?? string.Empty).Replace('\n', ' ').Trim();
                if (text.Length > 0)
                {
                    body.Inlines.Add(new Run(text));
                    body.Inlines.Add(new Run("  "));
                }
            }

            doc.Blocks.Add(body);
            return doc;
        }

        private void SetBibleFontSize(double size, bool syncSlider)
        {
            var clamped = Math.Max(BibleFontSizeMin, Math.Min(BibleFontSizeMax, size));
            _bibleFontSize = clamped;

            if (BibleReader != null)
                BibleReader.FontSize = clamped;

            if (BibleCompareList != null)
                BibleCompareList.FontSize = clamped;

            if (syncSlider && BibleFontSizeSlider != null && Math.Abs(BibleFontSizeSlider.Value - clamped) > 0.01)
                BibleFontSizeSlider.Value = clamped;

            if (BibleReader?.Document != null)
            {
                // Rebuild to keep verse-number sizing proportional.
                UpdateDisplayedVerses();
            }
        }

        private void BibleFontSmallerButton_Click(object sender, RoutedEventArgs e)
        {
            SetBibleFontSize(_bibleFontSize - 1, syncSlider: true);
        }

        private void BibleFontLargerButton_Click(object sender, RoutedEventArgs e)
        {
            SetBibleFontSize(_bibleFontSize + 1, syncSlider: true);
        }

        private void BibleFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            SetBibleFontSize(e.NewValue, syncSlider: false);
        }

        private void BibleReader_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

            e.Handled = true;
            var delta = e.Delta > 0 ? 1 : -1;
            SetBibleFontSize(_bibleFontSize + delta, syncSlider: true);
        }

        private async void BibleSearchButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchBibleAsync();
        }

        private void BibleSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            BibleSearchBox.Text = string.Empty;
            _bibleSearchResults.Clear();
            BibleSearchResults.Visibility = Visibility.Collapsed;
            BibleSearchStatus.Text = "Enter a keyword to search across scripture.";
        }

        private void BibleSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SearchBibleAsync();
            }
        }

        private async Task SearchBibleAsync()
        {
            var query = (BibleSearchBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                BibleSearchStatus.Text = "Please enter a keyword to search.";
                BibleSearchResults.Visibility = Visibility.Collapsed;
                return;
            }

            _bibleSearchCts?.Cancel();
            _bibleSearchCts = new CancellationTokenSource();
            var token = _bibleSearchCts.Token;

            const int limit = 200;
            var offlineReady = IsTranslationOfflineReady(_currentBibleTranslation);
            BibleSearchStatus.Text = offlineReady
                ? "Searching across the Bible…"
                : "Searching in the selected book…";
            BibleSearchResults.Visibility = Visibility.Collapsed;

            try
            {
                _bibleSearchResults.Clear();

                var found = 0;

                if (!offlineReady)
                {
                    var book = (BookCombo?.SelectedItem as BibleBookOption)?.Name;
                    if (string.IsNullOrWhiteSpace(book))
                        book = "Genesis";

                    BibleSearchStatus.Text = $"Searching in {book}… (Download offline to search the whole Bible)";

                    found = await SearchBibleWithinBookAsync(
                        book,
                        query,
                        limit,
                        found,
                        bookIndex: 1,
                        bookCount: 1,
                        token
                    );
                }
                else
                {
                    var books = _bibleBookOrder
                        .Where(b => _bibleChapterCounts.ContainsKey(b))
                        .ToList();

                    for (var i = 0; i < books.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (found >= limit)
                            break;

                        found = await SearchBibleWithinBookAsync(
                            books[i],
                            query,
                            limit,
                            found,
                            bookIndex: i + 1,
                            bookCount: books.Count,
                            token
                        );
                    }
                }

                if (token.IsCancellationRequested)
                    return;

                if (_bibleSearchResults.Count == 0)
                {
                    BibleSearchStatus.Text = offlineReady
                        ? "No matches found in the Bible."
                        : "No matches found in this book.";
                    BibleSearchResults.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var shown = _bibleSearchResults.Count;
                    if (offlineReady)
                    {
                        BibleSearchStatus.Text = shown >= limit
                            ? $"Showing first {shown} match(es) across the Bible."
                            : $"Found {shown} match(es) across the Bible.";
                    }
                    else
                    {
                        BibleSearchStatus.Text = shown >= limit
                            ? $"Showing first {shown} match(es) in this book."
                            : $"Found {shown} match(es) in this book.";
                    }
                    BibleSearchResults.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore; user started a new search.
            }
            catch (Exception ex)
            {
                BibleSearchStatus.Text = "Search error: " + ex.Message;
                BibleSearchResults.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<int> SearchBibleWithinBookAsync(
            string book,
            string query,
            int limit,
            int found,
            int bookIndex,
            int bookCount,
            CancellationToken token)
        {
            if (!_bibleChapterCounts.TryGetValue(book, out var chapterCount) || chapterCount < 1)
                return found;

            var q = query.Trim();
            if (q.Length == 0)
                return found;

            for (var chapter = 1; chapter <= chapterCount; chapter++)
            {
                token.ThrowIfCancellationRequested();

                // Keep UI responsive.
                if (chapter % 2 == 1)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        BibleSearchStatus.Text = $"Searching {book} {chapter}/{chapterCount} ({bookIndex}/{bookCount})… Found {found}…";
                    });
                }

                var verses = await FetchChapterVersesAsync(_currentBibleTranslation, book, chapter, token);
                foreach (var v in verses)
                {
                    token.ThrowIfCancellationRequested();

                    if (found >= limit)
                        return found;

                    if (v.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    found++;
                    var result = new BibleSearchResult
                    {
                        Book = book,
                        Chapter = chapter,
                        Verse = v.VerseNumber,
                        Reference = $"{book} {chapter}:{v.VerseNumber}",
                        Text = v.Text
                    };

                    // Show results incrementally.
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _bibleSearchResults.Add(result);
                        BibleSearchResults.Visibility = Visibility.Visible;
                        var scope = bookCount <= 1 ? "in this book" : "across the Bible";
                        BibleSearchStatus.Text = $"Found {found} match(es) {scope}…";
                    });
                }
            }

            return found;
        }

        private void BibleSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Intentionally empty; we navigate on mouse-up so clicking an already-selected
            // item still jumps to the verse.
        }

        private async void BibleSearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsFromScrollBar(e.OriginalSource as DependencyObject))
                return;

            if (BibleSearchResults?.SelectedItem is not BibleSearchResult r)
                return;

            e.Handled = true;

            // Stop any in-progress search from continuing to append results.
            try { _bibleSearchCts?.Cancel(); } catch { }

            await NavigateToBibleReferenceAsync(r.Book, r.Chapter, r.Verse);

            // Collapse results once a verse is chosen.
            try
            {
                BibleSearchResults.Visibility = Visibility.Collapsed;
                BibleSearchStatus.Text = $"Jumped to {r.Reference}.";
            }
            catch { }
        }

        private static bool IsFromScrollBar(DependencyObject? source)
        {
            var d = source;
            while (d != null)
            {
                if (d is ScrollBar)
                    return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private async Task NavigateToBibleReferenceAsync(string book, int chapter, int verse)
        {
            if (!_bibleSelectorsInitialized)
                InitializeBibleSelectors();

            if (BookCombo == null || ChapterCombo == null || VerseStartCombo == null || VerseEndCombo == null)
                return;

            _bibleNavigateCts?.Cancel();
            _bibleNavigateCts = new CancellationTokenSource();
            var token = _bibleNavigateCts.Token;

            try
            {
                var bookOpt = BookCombo.Items
                    .OfType<BibleBookOption>()
                    .FirstOrDefault(o => string.Equals(o.Name, book, StringComparison.OrdinalIgnoreCase));
                if (bookOpt != null)
                {
                    _suppressBookChange = true;
                    BookCombo.SelectedItem = bookOpt;
                    _suppressBookChange = false;
                }

                if (!_bibleChapterCounts.TryGetValue(book, out var chapterCount) || chapterCount < 1)
                    return;

                var clampedChapter = Math.Max(1, Math.Min(chapterCount, chapter));

                _suppressChapterChange = true;
                ChapterCombo.ItemsSource = Enumerable.Range(1, chapterCount).ToList();
                ChapterCombo.SelectedItem = clampedChapter;
                _suppressChapterChange = false;

                BibleStatusText.Text = $"Loading {book} {clampedChapter}:{verse}…";
                BibleStatusText.Visibility = Visibility.Visible;
                BibleReader.Visibility = Visibility.Collapsed;

                var verses = await FetchChapterVersesAsync(_currentBibleTranslation, book, clampedChapter, token);
                if (token.IsCancellationRequested)
                    return;

                _currentChapterVerses.Clear();
                _currentChapterVerses.AddRange(verses);

                var verseNumbers = verses.Select(v => v.VerseNumber).OrderBy(n => n).ToList();

                _suppressVerseChange = true;
                VerseStartCombo.ItemsSource = verseNumbers;
                VerseEndCombo.ItemsSource = verseNumbers;

                if (verseNumbers.Count > 0)
                {
                    var chosen = verseNumbers.Contains(verse) ? verse : verseNumbers.First();
                    VerseStartCombo.SelectedItem = chosen;
                    VerseEndCombo.SelectedItem = chosen;
                }
                _suppressVerseChange = false;

                UpdateDisplayedVerses();
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                BibleStatusText.Text = "Unable to jump to verse: " + ex.Message;
                BibleStatusText.Visibility = Visibility.Visible;
                BibleReader.Visibility = Visibility.Collapsed;
            }
        }

        private static string? CanonicalizeBibleBookName(string name)
        {
            var n = (name ?? string.Empty).Trim();
            if (n.Length == 0)
                return null;

            if (_bibleChapterCounts.ContainsKey(n))
                return n;

            // Common variants.
            if (string.Equals(n, "Song of Songs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "Canticles", StringComparison.OrdinalIgnoreCase))
                return "Song of Solomon";

            return null;
        }

        private static string NormalizeVerseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var s = text.Replace('\n', ' ').Replace('\r', ' ');
            while (s.Contains("  ", StringComparison.Ordinal))
                s = s.Replace("  ", " ", StringComparison.Ordinal);
            return s.Trim();
        }

        private async Task<int> ImportZefaniaXmlAsync(string translation, string sourcePath, int totalChapters, CancellationToken token)
        {
            var completed = 0;
            string? currentBook = null;
            var currentChapter = 0;
            var chapterVerses = new List<BibleVerseEntry>(128);

            using var fs = File.OpenRead(sourcePath);
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Prohibit
            };

            using var reader = XmlReader.Create(fs, settings);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.LocalName, "BIBLEBOOK", StringComparison.OrdinalIgnoreCase))
                    {
                        currentBook = CanonicalizeBibleBookName(reader.GetAttribute("bname") ?? string.Empty);
                    }
                    else if (string.Equals(reader.LocalName, "CHAPTER", StringComparison.OrdinalIgnoreCase))
                    {
                        chapterVerses.Clear();
                        int.TryParse(reader.GetAttribute("cnumber"), out currentChapter);
                    }
                    else if (string.Equals(reader.LocalName, "VERS", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(reader.GetAttribute("vnumber"), out var vnum);
                        var raw = reader.ReadElementContentAsString();
                        var t = NormalizeVerseText(raw);
                        if (currentBook != null && currentChapter > 0 && vnum > 0 && t.Length > 0)
                        {
                            chapterVerses.Add(new BibleVerseEntry { VerseNumber = vnum, Text = t });
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (string.Equals(reader.LocalName, "CHAPTER", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentBook != null && currentChapter > 0 && chapterVerses.Count > 0)
                        {
                            TryWriteCachedChapterToDisk(translation, currentBook, currentChapter, chapterVerses.ToList());
                            completed++;

                            if (completed % 5 == 0 || completed == totalChapters)
                            {
                                var c = completed;
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        BibleDownloadStatus.Text = $"Importing… {c}/{totalChapters} chapters";
                                        BibleDownloadProgress.Value = Math.Min(totalChapters, c);
                                    }
                                    catch { }
                                });
                            }
                        }
                    }
                }
            }

            return completed;
        }

        private async Task<int> ImportUsfxXmlAsync(string translation, string sourcePath, int totalChapters, CancellationToken token)
        {
            var completed = 0;
            string? currentBook = null;
            var currentChapter = 0;
            var currentVerse = 0;

            var chapterVerses = new List<BibleVerseEntry>(128);
            var verseText = new StringBuilder(256);

            void FlushVerse()
            {
                if (currentBook == null || currentChapter <= 0 || currentVerse <= 0)
                {
                    verseText.Clear();
                    return;
                }

                var t = NormalizeVerseText(verseText.ToString());
                verseText.Clear();
                if (t.Length == 0)
                    return;

                chapterVerses.Add(new BibleVerseEntry { VerseNumber = currentVerse, Text = t });
            }

            async Task FlushChapterAsync()
            {
                FlushVerse();

                if (currentBook != null && currentChapter > 0 && chapterVerses.Count > 0)
                {
                    TryWriteCachedChapterToDisk(translation, currentBook, currentChapter, chapterVerses.ToList());
                    completed++;

                    if (completed % 5 == 0 || completed == totalChapters)
                    {
                        var c = completed;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                BibleDownloadStatus.Text = $"Importing… {c}/{totalChapters} chapters";
                                BibleDownloadProgress.Value = Math.Min(totalChapters, c);
                            }
                            catch { }
                        });
                    }
                }

                chapterVerses.Clear();
                currentVerse = 0;
            }

            using var fs = File.OpenRead(sourcePath);
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = false,
                DtdProcessing = DtdProcessing.Prohibit
            };

            using var reader = XmlReader.Create(fs, settings);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.LocalName, "book", StringComparison.OrdinalIgnoreCase))
                    {
                        await FlushChapterAsync();

                        var id = reader.GetAttribute("id") ?? string.Empty;
                        currentBook = _usfxBookIdToName.TryGetValue(id, out var name) ? name : null;
                        currentChapter = 0;
                        continue;
                    }

                    if (currentBook == null)
                    {
                        if (string.Equals(reader.LocalName, "f", StringComparison.OrdinalIgnoreCase))
                            reader.Skip();
                        continue;
                    }

                    if (string.Equals(reader.LocalName, "c", StringComparison.OrdinalIgnoreCase))
                    {
                        await FlushChapterAsync();
                        int.TryParse(reader.GetAttribute("id"), out currentChapter);
                        continue;
                    }

                    if (string.Equals(reader.LocalName, "v", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushVerse();
                        int.TryParse(reader.GetAttribute("id"), out currentVerse);
                        continue;
                    }

                    if (string.Equals(reader.LocalName, "f", StringComparison.OrdinalIgnoreCase))
                    {
                        // Footnote: skip entirely.
                        reader.Skip();
                        continue;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    if (currentBook != null && currentChapter > 0 && currentVerse > 0)
                        verseText.Append(reader.Value);
                }
            }

            await FlushChapterAsync();
            return completed;
        }

        private async Task<int> ImportOsisXmlAsync(string translation, string sourcePath, int totalChapters, CancellationToken token)
        {
            var completed = 0;
            string? currentBook = null;
            var currentChapter = 0;
            var currentVerse = 0;
            var inVerse = false;

            var chapterVerses = new List<BibleVerseEntry>(128);
            var verseText = new StringBuilder(256);

            void FlushVerse()
            {
                if (!inVerse || currentBook == null || currentChapter <= 0 || currentVerse <= 0)
                {
                    verseText.Clear();
                    return;
                }

                var t = NormalizeVerseText(verseText.ToString());
                verseText.Clear();
                if (t.Length == 0)
                    return;

                chapterVerses.Add(new BibleVerseEntry { VerseNumber = currentVerse, Text = t });
            }

            async Task FlushChapterAsync()
            {
                FlushVerse();
                inVerse = false;
                currentVerse = 0;

                if (currentBook != null && currentChapter > 0 && chapterVerses.Count > 0)
                {
                    TryWriteCachedChapterToDisk(translation, currentBook, currentChapter, chapterVerses.ToList());
                    completed++;

                    if (completed % 5 == 0 || completed == totalChapters)
                    {
                        var c = completed;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                BibleDownloadStatus.Text = $"Importing… {c}/{totalChapters} chapters";
                                BibleDownloadProgress.Value = Math.Min(totalChapters, c);
                            }
                            catch { }
                        });
                    }
                }

                chapterVerses.Clear();
            }

            using var fs = File.OpenRead(sourcePath);
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = false,
                DtdProcessing = DtdProcessing.Prohibit
            };

            using var reader = XmlReader.Create(fs, settings);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.LocalName, "div", StringComparison.OrdinalIgnoreCase))
                    {
                        var type = reader.GetAttribute("type");
                        if (string.Equals(type, "book", StringComparison.OrdinalIgnoreCase))
                        {
                            var id = reader.GetAttribute("osisID") ?? string.Empty;
                            currentBook = _osisBookIdToName.TryGetValue(id, out var name) ? name : null;
                            continue;
                        }
                    }

                    if (string.Equals(reader.LocalName, "chapter", StringComparison.OrdinalIgnoreCase))
                    {
                        var nAttr = reader.GetAttribute("n") ?? string.Empty;
                        if (int.TryParse(nAttr, out var ch))
                        {
                            if (currentBook != null && currentChapter != 0 && ch != currentChapter)
                                await FlushChapterAsync();
                            currentChapter = ch;
                        }
                        continue;
                    }

                    if (string.Equals(reader.LocalName, "verse", StringComparison.OrdinalIgnoreCase))
                    {
                        var osisId = reader.GetAttribute("osisID");
                        var eId = reader.GetAttribute("eID");

                        if (!string.IsNullOrWhiteSpace(osisId))
                        {
                            // Start of verse milestone.
                            FlushVerse();
                            inVerse = true;

                            var nAttr = reader.GetAttribute("n") ?? string.Empty;
                            if (!int.TryParse(nAttr, out currentVerse))
                            {
                                currentVerse = 0;
                                var parts = osisId.Split('.', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 3)
                                    int.TryParse(parts[2], out currentVerse);
                            }
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(eId))
                        {
                            // End of verse milestone.
                            FlushVerse();
                            inVerse = false;
                            continue;
                        }
                    }

                    if (string.Equals(reader.LocalName, "note", StringComparison.OrdinalIgnoreCase))
                    {
                        // Notes are not part of verse text.
                        reader.Skip();
                        continue;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    if (inVerse && currentBook != null && currentChapter > 0 && currentVerse > 0)
                        verseText.Append(reader.Value);
                }
            }

            await FlushChapterAsync();
            return completed;
        }

        private async Task<List<BibleVerseEntry>> FetchChapterVersesAsync(string translation, string book, int chapter, CancellationToken token)
        {
            var cacheKey = $"{translation}|{book}|{chapter}";
            if (_bibleChapterCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var diskCached = TryReadCachedChapterFromDisk(translation, book, chapter);
            if (diskCached != null)
            {
                _bibleChapterCache[cacheKey] = diskCached;
                return diskCached;
            }

            await _bibleApiGate.WaitAsync(token);
            try
            {
                // Ensure we don't hit the public rate limit.
                var elapsedMs = (DateTime.UtcNow - _bibleApiLastRequestUtc).TotalMilliseconds;
                var waitMs = (int)Math.Max(0, BibleApiMinDelayMs - elapsedMs);
                if (waitMs > 0)
                    await Task.Delay(waitMs, token);

                var encoded = Uri.EscapeDataString($"{book} {chapter}");
                var url = $"https://bible-api.com/{encoded}?translation={Uri.EscapeDataString(translation)}";

                for (var attempt = 1; attempt <= 4; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    _bibleApiLastRequestUtc = DateTime.UtcNow;

                    if ((int)response.StatusCode == 429)
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta;
                        var delay = retryAfter ?? TimeSpan.FromMilliseconds(2500 + (attempt * 750));
                        await Task.Delay(delay, token);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    var data = await JsonSerializer.DeserializeAsync<BibleApiResponse>(stream, _bibleJsonOptions, token);
                    if (data?.Verses == null || data.Verses.Count == 0)
                    {
                        var empty = new List<BibleVerseEntry>();
                        _bibleChapterCache[cacheKey] = empty;
                        return empty;
                    }

                    var verses = data.Verses
                        .OrderBy(v => v.Verse)
                        .Select(v => new BibleVerseEntry
                        {
                            VerseNumber = v.Verse,
                            Text = v.Text?.Replace('\n', ' ').Trim() ?? string.Empty
                        })
                        .ToList();

                    _bibleChapterCache[cacheKey] = verses;
                    TryWriteCachedChapterToDisk(translation, book, chapter, verses);
                    return verses;
                }

                throw new HttpRequestException("Rate limited (429). Please wait a moment and try again.");
            }
            finally
            {
                _bibleApiGate.Release();
            }
        }

        private List<BibleVerseEntry>? TryReadCachedChapterFromDisk(string translation, string book, int chapter)
        {
            try
            {
                var file = GetBibleCacheFilePath(translation, book, chapter);
                if (!File.Exists(file))
                    return null;

                var json = File.ReadAllText(file);
                var verses = JsonSerializer.Deserialize<List<BibleVerseEntry>>(json, _bibleJsonOptions);
                return verses ?? null;
            }
            catch
            {
                return null;
            }
        }

        private void TryWriteCachedChapterToDisk(string translation, string book, int chapter, List<BibleVerseEntry> verses)
        {
            try
            {
                var file = GetBibleCacheFilePath(translation, book, chapter);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var json = JsonSerializer.Serialize(verses, _bibleJsonOptions);
                File.WriteAllText(file, json);
            }
            catch
            {
                // ignore
            }
        }

        private string GetBibleCacheFilePath(string translation, string book, int chapter)
        {
            var safeBook = MakeSafePathSegment(book);
            var safeTranslation = MakeSafePathSegment(translation);
            return Path.Combine(_bibleCacheRootDir, safeTranslation, safeBook, $"{chapter}.json");
        }

        private string GetOfflineCompleteMarkerPath(string translation)
        {
            var safeTranslation = MakeSafePathSegment(translation);
            return Path.Combine(_bibleCacheRootDir, safeTranslation, BibleOfflineCompleteMarkerFile);
        }

        private bool IsTranslationOfflineReady(string translation)
        {
            try
            {
                return File.Exists(GetOfflineCompleteMarkerPath(translation));
            }
            catch
            {
                return false;
            }
        }

        private static string MakeSafePathSegment(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static readonly Dictionary<string, int> _bibleChapterCounts = new(StringComparer.OrdinalIgnoreCase)
        {
            {"Genesis", 50}, {"Exodus", 40}, {"Leviticus", 27}, {"Numbers", 36}, {"Deuteronomy", 34},
            {"Joshua", 24}, {"Judges", 21}, {"Ruth", 4}, {"1 Samuel", 31}, {"2 Samuel", 24}, {"1 Kings", 22}, {"2 Kings", 25},
            {"1 Chronicles", 29}, {"2 Chronicles", 36}, {"Ezra", 10}, {"Nehemiah", 13}, {"Esther", 10}, {"Job", 42},
            {"Psalms", 150}, {"Proverbs", 31}, {"Ecclesiastes", 12}, {"Song of Solomon", 8}, {"Isaiah", 66}, {"Jeremiah", 52},
            {"Lamentations", 5}, {"Ezekiel", 48}, {"Daniel", 12}, {"Hosea", 14}, {"Joel", 3}, {"Amos", 9}, {"Obadiah", 1},
            {"Jonah", 4}, {"Micah", 7}, {"Nahum", 3}, {"Habakkuk", 3}, {"Zephaniah", 3}, {"Haggai", 2}, {"Zechariah", 14},
            {"Malachi", 4}, {"Matthew", 28}, {"Mark", 16}, {"Luke", 24}, {"John", 21}, {"Acts", 28}, {"Romans", 16},
            {"1 Corinthians", 16}, {"2 Corinthians", 13}, {"Galatians", 6}, {"Ephesians", 6}, {"Philippians", 4},
            {"Colossians", 4}, {"1 Thessalonians", 5}, {"2 Thessalonians", 3}, {"1 Timothy", 6}, {"2 Timothy", 4},
            {"Titus", 3}, {"Philemon", 1}, {"Hebrews", 13}, {"James", 5}, {"1 Peter", 5}, {"2 Peter", 3}, {"1 John", 5},
            {"2 John", 1}, {"3 John", 1}, {"Jude", 1}, {"Revelation", 22}
        };

        private sealed class BibleApiResponse
        {
            [JsonPropertyName("verses")]
            public List<BibleApiVerse> Verses { get; set; } = new();
        }

        private sealed class BibleApiVerse
        {
            [JsonPropertyName("verse")]
            public int Verse { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;

            [JsonPropertyName("book_name")]
            public string BookName { get; set; } = string.Empty;

            [JsonPropertyName("chapter")]
            public int Chapter { get; set; }
        }

        private sealed class BibleVerseEntry
        {
            public int VerseNumber { get; init; }
            public string Text { get; init; } = string.Empty;
            public string Display => $"{VerseNumber}. {Text}";
        }

        private sealed class BibleSearchResult
        {
            public string Book { get; init; } = string.Empty;
            public int Chapter { get; init; }
            public int Verse { get; init; }
            public string Reference { get; init; } = string.Empty;
            public string Text { get; init; } = string.Empty;
        }
    }

    // Minimal in-code rename dialog (theme-safe)
    public class RenameDialog : Window
    {
        private readonly TextBox _txt;

        public string? Result { get; private set; }

        public RenameDialog(string title, string initialValue)
        {
            Title = title;
            Width = 420;
            Height = 230;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            // Theme-safe background (works even if keys are missing)
            Background = TryBrush("BgCard") ?? new SolidColorBrush(Color.FromRgb(23, 24, 29));
            Foreground = TryBrush("TextMain") ?? Brushes.White;

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Rename display name",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            root.Children.Add(header);

            var hint = new TextBlock
            {
                Text = "This only changes how the PDF appears in the list. The original file stays untouched.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryBrush("TextMuted") ?? new SolidColorBrush(Color.FromRgb(180, 186, 198)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(hint, 1);
            root.Children.Add(hint);

            var inputBorder = new Border
            {
                Background = TryBrush("BgCardSoft") ?? new SolidColorBrush(Color.FromRgb(29, 31, 37)),
                BorderBrush = TryBrush("BorderSoft") ?? new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2)
            };

            _txt = new TextBox
            {
                Text = initialValue,
                MinHeight = 42,
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.Transparent,
                Foreground = TryBrush("TextMain") ?? Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 15
            };
            inputBorder.Child = _txt;

            Grid.SetRow(inputBorder, 2);
            root.Children.Add(inputBorder);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancel = CreateDialogButton(
                "Cancel",
                TryBrush("BgCardSoft") ?? new SolidColorBrush(Color.FromRgb(36, 39, 47)),
                TryBrush("TextMain") ?? Brushes.White,
                borderBrush: TryBrush("BorderSoft") ?? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
            );
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.IsCancel = true;
            cancel.Click += (_, __) => { DialogResult = false; Close(); };

            var ok = CreateDialogButton(
                "Save name",
                TryBrush("AccentPrimary") ?? new SolidColorBrush(Color.FromRgb(66, 133, 244)),
                Brushes.White,
                isPrimary: true
            );
            ok.Margin = new Thickness(0);
            ok.IsDefault = true;
            ok.Click += (_, __) => Accept();

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            Content = root;

            Loaded += (_, __) =>
            {
                _txt.Focus();
                _txt.SelectAll();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
                if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
            };
        }

        private void Accept()
        {
            Result = _txt.Text;
            DialogResult = true;
            Close();
        }

        private Button CreateDialogButton(string text, Brush background, Brush foreground, Brush? borderBrush = null, bool isPrimary = false)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 110,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(16, 8, 16, 8),
                Background = background,
                Foreground = foreground,
                BorderBrush = borderBrush ?? background,
                BorderThickness = new Thickness(1),
                FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
                Cursor = Cursors.Hand
            };

            button.Resources[SystemParameters.FocusVisualStyleKey] = null;
            return button;
        }

        private static Brush? TryBrush(string key)
        {
            try
            {
                return Application.Current?.Resources[key] as Brush;
            }
            catch
            {
                return null;
            }
        }
    }
}
