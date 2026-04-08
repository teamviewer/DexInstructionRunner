// MainWindow.axaml.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DexInstructionRunner.Helpers;
using DexInstructionRunner.Models;
using DexInstructionRunner.Services;
using DexInstructionRunner.Services.ChartRenderers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaColor = Avalonia.Media.Color;
using DexInstructionRunner.Views;


namespace DexInstructionRunner
{
    public class InstructionHistoryItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Created { get; set; }
        public string Status { get; set; }
        public int DefinitionId { get; set; }
    }

    public class InstructionDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<Parameter> Parameters { get; set; }
        public string Description { get; set; }
        public ResponseTemplateConfiguration ResponseTemplateConfiguration { get; set; }
        public AggregationConfig Aggregation { get; set; }

        // ✅ Schema columns (used to drive server-side results filtering UI)
        public List<InstructionSchemaEntry> Schema { get; set; }

        // ✅ New TTL fields from instruction metadata
        public int InstructionTtlMinutes { get; set; }
        public int ResponseTtlMinutes { get; set; }
        public int MinimumInstructionTtlMinutes { get; set; }
        public int MaximumInstructionTtlMinutes { get; set; }
        public int MinimumResponseTtlMinutes { get; set; }
        public int MaximumResponseTtlMinutes { get; set; }
    }

    public class InstructionSchemaEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Length { get; set; }
        public string RenderAs { get; set; }
    }



    public class ResponseTemplateConfiguration
    {
        public string Name { get; set; }
        public List<TemplateConfiguration> TemplateConfigurations { get; set; }
        public List<PostProcessor> PostProcessors { get; set; }
    }

    public class TemplateConfiguration
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string X { get; set; }
        public string Y { get; set; }
        public string Z { get; set; }
        public string PostProcessor { get; set; }
        public int Size { get; set; }
        public int Row { get; set; }
    }

    public class PostProcessor
    {
        public string Name { get; set; }
        public string Function { get; set; }
    }

    public class AggregationConfig
    {
        public List<AggregationSchemaEntry> Schema { get; set; }
        public string GroupBy { get; set; }
        public List<AggregationOperation> Operations { get; set; }
    }

    public class AggregationSchemaEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Length { get; set; }
        public string RenderAs { get; set; }
    }

    public class AggregationOperation
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public partial class MainWindow : Window
    {
        private sealed class PlatformRowView : INotifyPropertyChanged
        {
            private string _alias = string.Empty;
            private string _obfuscatedUrl = string.Empty;
            private string _urlPlain = string.Empty;
            private string _urlEdit = string.Empty;
            private string _defaultMarker = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;

            public bool IsNewRow { get; set; }
            public bool OriginalWasEncrypted { get; set; }
            public string OriginalUrlRaw { get; set; } = string.Empty; // may be plaintext or enc:...

            // UI-only indicator used by Settings grid. This is NOT persisted per-row.
            public string DefaultMarker
            {
                get => _defaultMarker;
                set
                {
                    _defaultMarker = value ?? string.Empty;
                    OnPropertyChanged(nameof(DefaultMarker));
                }
            }

            public string UrlPlain
            {
                get => _urlPlain;
                set
                {
                    _urlPlain = value ?? string.Empty;
                    ObfuscatedUrl = Obfuscate(_urlPlain);
                    OnPropertyChanged(nameof(UrlPlain));
                }
            }

            public string Alias
            {
                get => _alias;
                set
                {
                    _alias = value ?? string.Empty;
                    OnPropertyChanged(nameof(Alias));
                }
            }

            public string ObfuscatedUrl
            {
                get => _obfuscatedUrl;
                private set
                {
                    _obfuscatedUrl = value ?? string.Empty;
                    OnPropertyChanged(nameof(ObfuscatedUrl));
                }
            }

            // The user can type a new URL here (never pre-filled; never reveals old URL)
            public string UrlEdit
            {
                get => _urlEdit;
                set
                {
                    _urlEdit = value ?? string.Empty;
                    OnPropertyChanged(nameof(UrlEdit));
                }
            }

            // Legacy property (kept for compatibility with older bindings, but not used by current XAML)
            public bool CanRemove => !IsNewRow;

            private void OnPropertyChanged(string name)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private static string Obfuscate(string url)
            {
                url ??= string.Empty;
                url = url.Trim();
                if (url.Length == 0)
                    return string.Empty;
                if (url.Length == 1)
                    return "*";
                return new string('*', url.Length - 1) + url[^1];
            }
        }

        private sealed class PlatformDisplay
        {
            public string Url { get; }
            public string Alias { get; }

            public PlatformDisplay(string url, string alias)
            {
                Url = url ?? string.Empty;
                Alias = string.IsNullOrWhiteSpace(alias) ? Url : alias;
            }

            public override string ToString() => Alias;
        }

        private TextBlock _statusLabel;
        private TextBox _logTextBox;
        private ComboBox _managementGroupComboBox;
        private ComboBox _matchTypeComboBox;
        private TextBox _fqdnTextBox;
        private ListBox? _instructionFqdnSearchResultsListBox;
        private Border? _instructionFqdnSearchResultsBorder;
        private CancellationTokenSource? _instructionFqdnSearchCts;
        private bool _suppressFqdnSuggest;
        private Border? _fqdnManagerBorder;
        private TextBox? _fqdnManagerSearchTextBox;
        private RadioButton? _fqdnManagerSearchByFqdnRadioButton;
        private RadioButton? _fqdnManagerSearchByUserRadioButton;
        private Avalonia.Controls.DataGrid? _fqdnManagerResultsGrid;
        private ListBox? _fqdnManagerSelectedListBox;
        private TextBlock? _fqdnManagerStatusText;
        private TextBlock? _fqdnManagerSelectedHeaderText;
        private readonly ObservableCollection<string> _fqdnManagerSelectedItems = new();
        private CancellationTokenSource? _fqdnManagerSearchCts;
        private TextBox _searchBox;

        // FQDN List tab instruction selector controls
        private TextBox _fqdnInstructionSearchBox;
        private ListBox _fqdnInstructionListBox;
        private TextBlock _fqdnInstructionDescriptionText;

        // Results total row hint (from Combined stats)
        private int _resultsTotalRowsExpected = 0;

        // Results TTL countdown (based on the TTL used when the instruction was started)
        private DateTime _currentInstructionRunStartedUtc = DateTime.MinValue;
        private int _currentInstructionTtlMinutes = 0;
        private TextBlock _resultsTtlCountdownText;
        private Button _runButton;
        private Button _exportButton;
        private Button _exportAllButton;

        // Results tab option: when sending FQDNs to the FQDN List, fetch + filter across ALL pages (not just current page)
        // Results tab option: when enabled, apply Results filtering on the server (requery page 1)
        // instead of only filtering the currently loaded page.
        private CheckBox? _resultsServerFilterAllCheckBox;

        // Results context coverage label override from the server (prevents generic "All devices" text while polling)
        private string? _resultsCoverageOverride;
        private int _resultsCoverageOverrideInstructionId;

        // Run-scope server-side results filter (schema-driven)
        private RadioButton? _runScopeAllRadio;
        private RadioButton? _runScopeFilteredRadio;
        private Button? _instrRunFilterButton;
        private AutoCompleteBox? _instrFilterCol1;
        private AutoCompleteBox? _instrFilterCol2;
        private AutoCompleteBox? _instrFilterCol3;
        private AutoCompleteBox? _instrFilterCol4;
        private AutoCompleteBox? _instrFilterCol5;
        private TextBox? _instrFilterVal1;
        private TextBox? _instrFilterVal2;
        private TextBox? _instrFilterVal3;
        private TextBox? _instrFilterVal4;
        private TextBox? _instrFilterVal5;
        private TextBlock? _instrFilterSummaryText;
        private Button? _instrFilterClearButton;

        // Flyout buttons (Instruction Detail)
        private Button? _instrRunFilterApplyButton;
        private Button? _instrRunFilterCancelButton;
        private Button? _instrRunFilterClearButton;

        private RadioButton? _fqdnRunScopeAllRadio;
        private RadioButton? _fqdnRunScopeFilteredRadio;
        private Button? _fqdnRunFilterButton;
        private AutoCompleteBox? _fqdnFilterCol1;
        private AutoCompleteBox? _fqdnFilterCol2;
        private AutoCompleteBox? _fqdnFilterCol3;
        private AutoCompleteBox? _fqdnFilterCol4;
        private AutoCompleteBox? _fqdnFilterCol5;
        private TextBox? _fqdnFilterVal1;
        private TextBox? _fqdnFilterVal2;
        private TextBox? _fqdnFilterVal3;
        private TextBox? _fqdnFilterVal4;
        private TextBox? _fqdnFilterVal5;
        private TextBlock? _fqdnFilterSummaryText;
        private Button? _fqdnFilterClearButton;

        // Flyout buttons (FQDN List)
        private Button? _fqdnRunFilterApplyButton;
        private Button? _fqdnRunFilterCancelButton;
        private Button? _fqdnRunFilterClearButton;
        private readonly Dictionary<string, string> _selectedTargetPrimaryUsers = new(StringComparer.OrdinalIgnoreCase);
        // Active (applied) run filter clauses per tab. The flyout edits are only committed when the user clicks Set.
        private readonly List<RunResultFilterClause> _activeInstrRunFilters = new();
        private readonly List<RunResultFilterClause> _activeFqdnRunFilters = new();

        // Draft rows bound to the Run Result Filter flyouts (instruction detail + FQDN list).
        // Rows exist ONLY when the user adds them (fully dynamic, schema-driven).
        public ObservableCollection<RunResultFilterRow> InstrRunFilterRows { get; } = new();
        public ObservableCollection<RunResultFilterRow> FqdnRunFilterRows { get; } = new();

        // Schema-driven column list (shared by both flyouts).
        public ObservableCollection<string> RunFilterAvailableColumns { get; } = new();

        // Column name -> raw schema type (case-insensitive).
        private readonly Dictionary<string, string> _runFilterColumnTypeMap = new(StringComparer.OrdinalIgnoreCase);

        private List<string> _currentInstructionSchemaColumns = new();
        private int _currentInstructionSchemaDefinitionId = -1;

        // Settings: whether "Send to FQDN List" auto-switches to the FQDN List tab.
        // Default: true (matches current behavior).
        private bool _switchToFqdnTabAfterSend = true;
        private Button _refreshHistoryButton;
        private ListBox _instructionListBox;
        private ListBox _historyListBox;
        private Button _loginButton;
        private string _consumerName;
        private string _token;
        private string _principalName;
        private ComboBox _chartComboBox;
        private List<JObject> _chartConfigs = new();
        private Dictionary<string, int> _instructionMap = new();
        private List<InstructionDefinition> _instructionDefinitions = new();
        private bool _isRenderingResults = false;
        private string _currentViewMode = "Raw"; // or whatever it was last set to

        private bool _suppressResultsViewRadioChanged = false;
        private List<Dictionary<string, string>> _parsedResults = new();
        private Dictionary<string, CheckBox> _fqdnCheckboxes = new();
        private readonly HashSet<string> _selectedResultFqdns = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, TextBox> _columnFilters = new();
        // Preserve user-entered filter text across refreshes/auto-refresh.
        // Keyed by column name (case-insensitive) for stability.
        private readonly Dictionary<string, string> _resultsFilterText = new(StringComparer.OrdinalIgnoreCase);
        private string? _currentSortColumn = null;
        private bool _sortAscending = true;
        private TextBox _instructionTtlBox;
        private TextBox _responseTtlBox;
        private bool _showLogOnStartup = false;
        private IConfigurationRoot _config;
        private TextBlock _tokenTimerText;
        private TokenStoreService _tokenStoreService;
        private bool _isProgressFetched = false;
        private List<InstructionHistoryItem> _instructionHistory = new();
        private ListBox _fqdnListBox;
        private AuthenticationService _authService;
        private TextBlock FqdnListCountLabel => this.FindControl<TextBlock>("FqdnListCountLabel");
        private TextBlock _authStatusText;
        private StackPanel _topLoadingPanel;
        private Button _logoutButton;
        private List<FilterGroup> _filterGroups = new();
        private FilterGroup _currentGroup = new(); // Default active group
        private ExperienceService _experienceService;
        private bool _experienceFiltersLoaded = false;
        private List<Dictionary<string, string>> _rawResults = new();
        private List<Dictionary<string, string>> _filteredResults = new();
        private string _defaultMG;
        private int _lastInstructionId;
        private InstructionDefinition? _lastInstructionDefinition;

        // Track which instruction the Results UI was last fully bound to. Used to avoid tearing down and rebuilding
        // the Results panels during simple refreshes (page size, paging, server-filter requery, etc.).
        private int _resultsUiBoundInstructionId = -1;
        private DatePicker _historyDatePicker;
        private Button _resetFiltersButton;
        private StackPanel _resultsPanel;


        private string? _resultsChartLastSelectedLabel;
        private int? _resultsChartLastSelectedIndex;
        private Button _resultsPrevPageButton;
        private Button _resultsNextPageButton;
        private TextBlock _resultsPageInfoText;
        private ComboBox _resultsPageSizeCombo;

        // Results paging state (for display)
        private string? _resultsCurrentRange = "0;0";
        private string? _resultsNextRange = null;
        private readonly Stack<string> _resultsPrevRanges = new();
        private int _resultsPageIndex = 0;
        private int _resultsPageSize = 20;
        private bool _resultsPagingInitialized = false;

        private Button _rerunInstructionButton;
        private Button _cancelInstructionButton;
        private ComboBox _exportFormatComboBox;

        private readonly HashSet<string> _expiredRerunPromptShownForInstructionIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _expiredRerunPromptInFlight = false;
        private DateTime _lastHistoryDoubleTapUtc = DateTime.MinValue;

        private int _instructionFqdnSearchStart = 1;
        private const int _instructionFqdnSearchPageSize = 50;
        private int _instructionFqdnSearchLastCount = 0;
        private string _lastInstructionFqdnSearchRaw = string.Empty;
        private readonly HashSet<long> _authPromptShownForInstructionIds = new();

        private string GetConfiguredExportFormatOrDefault(string fallback)
        {
            try
            {
                // The app's single source of truth is the Settings tab "Default export format" dropdown.
                // The ComboBox uses ComboBoxItem items with Content like "csv", "tsv", "xlsx".
                if (_exportFormatComboBox == null)
                    _exportFormatComboBox = this.FindControl<ComboBox>("DefaultExportFormatComboBox");

                object sel = _exportFormatComboBox?.SelectedItem;

                string fmt = null;

                if (sel is ComboBoxItem cbi)
                    fmt = cbi.Content?.ToString();
                else if (sel != null)
                    fmt = sel.ToString();

                if (string.IsNullOrWhiteSpace(fmt))
                {
                    // Fall back to config helper if we have no UI selection (e.g., early startup)
                    try
                    {
                        LoadConfig();
                        // _config is set by LoadConfig() as an IConfigurationRoot
                        fmt = _config?["DefaultExportFormat"];
                        if (string.IsNullOrWhiteSpace(fmt))
                            fmt = _config?["ExportOptions:DefaultExportFormat"];
                        if (string.IsNullOrWhiteSpace(fmt))
                            fmt = _config?["Export:DefaultFormat"];

                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(fmt))
                    return fallback;

                fmt = fmt.Trim().TrimStart('.').ToLowerInvariant();

                if (fmt == "csv") return "csv";
                if (fmt == "tsv") return "tsv";
                if (fmt == "xlsx") return "xlsx";

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void UpdateDefaultExportFormatLabel()
        {
            try
            {
                var label = TryFindControl<TextBlock>("DefaultExportFormatLabel") ?? this.FindControl<TextBlock>("DefaultExportFormatLabel");

                // Prefer the Settings tab selection (DefaultExportFormatComboBox) as the source of truth.
                string fmt = GetConfiguredExportFormatOrDefault("csv");

                if (label != null)
                    label.Text = $"({fmt})";
            }
            catch
            {
                // ignore
            }
        }

        private bool _isRunning = false;
        private InstructionDefinition _selectedInstruction;
        private Dictionary<string, Control> _parameterInputs = new();
        private Dictionary<string, Control> _fqdnParameterInputs = new();
        private List<DexInstructionRunner.Models.PlatformConfig> _platformConfigs = new();
        private DexInstructionRunner.Models.PlatformConfig _selectedPlatform;
        private ComboBox _platformUrlDropdown;
        private bool _isLegacyVersion = false;
        private string? _currentInstructionId = null;
        private ComboBox _targetingModeComboBox;
        private TextBox? _filterValueTextBox;
        private List<FilterAttributeDefinition> _allAttributes = new();
        private Button _OnPreviewTargetsClicked;
        private Button? _clearFqdnListButton;
        private Button? _removeSelectedFqdnButton;
        private TextBlock? _resultsFqdnListCountText;
        private TextBlock? _experienceFqdnListCountText;
        private RadioButton _dynamicTargetingRadio;
        private StackPanel _dynamicTargetingPanel;
        private StackPanel _fqdnRowPanel; // This is the panel that contains FQDN text box + match type + MG
        private ConfigHelper _configHelper;

        private ObservableCollection<PlatformRowView> _settingsPlatformRows = new ObservableCollection<PlatformRowView>();
        private bool _platformSettingsDirty;
        private string? _currentDefaultPlatformAlias;
        private string? _pendingDefaultPlatformAlias;

        private StackPanel _progressSummaryPanel;
        private TextBlock _progressSummaryLabel;
        private TextBlock _progressCountsLabel;
        private bool _hasPreviewedTargets = false;
        public int MinimumInstructionTtlMinutes { get; set; } = 10;
        public int MaximumInstructionTtlMinutes { get; set; } = 10080;
        public int MinimumResponseTtlMinutes { get; set; } = 10;
        public int MaximumResponseTtlMinutes { get; set; } = 10080;
        private StackPanel _loadingStatusPanel;
        private ProgressBar _resultsLoadingProgressBar;
        private TextBlock _resultsLoadingStatusText;
        private bool _resultsPageLoading;
        private bool _resultsProgressCompletedSticky;
        private int _resultsOutstandingCount;
        private bool _suppressPlatformSelectionChanged;
        private readonly Dictionary<string, string> _sessionTokenByPlatformUrl = new(StringComparer.OrdinalIgnoreCase);
        private bool _authExpiredDialogShown;


        // Progress stats -> auto-refresh Results when counts change
        private int _lastProgressReceivedCount = -1;
        private int _lastProgressOutstandingCount = -1;

        // Canonical "active" instruction id for the Results tab live polling.
        // We do NOT rely on ListBox selection state or history refresh triggers.
        private int _activeResultsInstructionId = -1;

        // Prefer the API-provided expiration timestamp when available.
        private DateTime? _activeResultsExpiresUtc;

        // Periodic refresh while TTL is still active (even if received does not change).
        private DateTime _lastPeriodicResultsRefreshUtc = DateTime.MinValue;

        // Track the last sent/received we displayed so status updates are stable.
        private int _lastProgressSentCount = -1;
        private DateTime _lastAutoResultsRefreshUtc = DateTime.MinValue;
        private DateTime _lastResultsReceivedIncreaseUtc = DateTime.MinValue;
        private bool _autoResultsRefreshInFlight;
        private Button _setDefaultMgButton;
        private ListBox _selectedFqdnListBox;
        private ComboBox _filterAttributeComboBox;
        private ComboBox _filterOperatorComboBox;
        private ComboBox _filterValueComboBox;
        private Button _addFilterButton;
        private WrapPanel _filterSummaryPanel;
        private string _logLevel = "Info";  // Default to Info
        private List<string> _dynamicAttributes = new(); // from API
        private Dictionary<string, List<string>> _attributeValueMap = new(); // OsType -> [Windows, macOS, Linux]
        private Dictionary<FilterGroup, WrapPanel> _groupToChipPanel = new();
        private Dictionary<FilterGroup, WrapPanel> _filterGroupToPanelMap = new();
        private ToggleSwitch _breakdownModeToggle;
        private JObject? _lastPreviewResult;
        private TextBlock _breakdownLabel;
        private Dictionary<string, string> _columnSortStates = new(); // "Asc", "Desc", or ""
        private TextBox _experienceScoreFqdnTextBox;
        private StackPanel _experienceResultsPanel;
        private HashSet<string> _selectedExperienceFqdns = new();
        private Dictionary<string, string> _experienceSortSelections = new();
        private Dictionary<string, string> _experienceFilterSelections = new();
        private bool _experienceSortAscending = true;
        private List<Dictionary<string, object>> _latestExperienceResults = new();
        private Dictionary<string, List<string>> _cachedExperienceFilters = new();
        private readonly MeasureService _measureService = new(new HttpClient());
        private List<ExperienceMeasure> _experienceMeasures = new();
        private bool _experienceTabInitialized = false;
        private HashSet<string> _selectedMeasureTitles = new();
        private MetricsHelper _metricsListBox;
        private MetricsHelper _metricsHelper;
        private List<Dictionary<string, string>> _experienceResults = new();

        // Experience UI paging/limit (keeps manual panel rendering responsive)
        private const int ExperienceMaxPageSize = 500;
        private int _experiencePageSize = 250;

        // Experience server paging state (Bookmark-based)
        private readonly Stack<string?> _experiencePrevBookmarks = new();
        private string? _experienceRequestBookmark = null; // bookmark used to fetch the current page (null for page 1)
        private string? _experienceNextBookmark = null;    // bookmark returned from the API for the next page
        private int _experiencePageIndex = 1;
        private int _experienceTotalCount = 0;

        private bool _experienceLastQueryUsedFqdns = false;
        private bool _experienceAllowEmptyFilterQuery = false;// allows "all" browsing with no explicit filters
        private List<string> _lastUsedExperienceFqdns = new();
        private Dictionary<string, TextBox> _experienceFilters = new();
        private HashSet<string> _experienceSortColumns = new();
        private Dictionary<string, CheckBox> _experienceFqdnCheckboxes = new();
        private Dictionary<string, TextBox> _experienceColumnFilters = new();

        // Preserve Experience filter text across score refreshes.
        private readonly Dictionary<string, string> _experienceFilterText = new(StringComparer.OrdinalIgnoreCase);
        private List<Dictionary<string, object>> _filteredExperienceResults;
        private DispatcherTimer _notificationTimer;
        private bool _notificationTimerWired;
        private List<ApprovalInstruction> _pendingApprovals = new();
        private ComboBox _coverageTagNameComboBox;
        private ComboBox _coverageTagValueComboBox;
        private string _defaultManagementGroup = "AutoPilot"; // or fetch from config
        private List<ExperienceFilter> _activeExperienceFilters = new(); // initialized to empty
        private Dictionary<string, List<string>> _activeExperienceFiltersDict;
        private TextBox _experienceFqdnBox;
        private Dictionary<string, CheckBox> _experienceCheckboxMap = new();
        private Dictionary<string, TextBox> _experienceFilterInputs = new();
        private Dictionary<string, List<string>> _lastUsedExperienceFilters;
        private List<string> _lastUsedExperienceMetrics;
        private ComboBox? _deviceStatusFilterCombo;
        private DeviceExplorerStatusFilter _deviceExplorerStatusFilter = DeviceExplorerStatusFilter.All;
        private bool _suppressResultsAutoFilterApply = false;
        private bool _suppressExperienceAutoFilterApply = false;
        private bool _enableManagementGroupTargeting = false;
        private string? _lastTargetingModeForClear;

        private T? TryFindControl<T>(string name) where T : Avalonia.Controls.Control
        {
            try
            {
                return this.FindControl<T>(name);
            }
            catch
            {
                return null;
            }
        }


        private void PopulateTargetingModeComboBox()
        {
            if (_targetingModeComboBox == null)
                return;

            // Helpdesk mode defaults to FQDN. MG is only available when enabled via config.
            var items = new List<string> { "FQDN" };
            if (_enableManagementGroupTargeting)
                items.Add("Management Group");

            _targetingModeComboBox.ItemsSource = items;
            _targetingModeComboBox.SelectedIndex = 0; // default FQDN

            // Ensure the panels reflect the initial selection.
            UpdateTargetingModePanels("FQDN");
        }

        private void UpdateTargetingModePanels(string selectedMode)
        {
            var mgPanel = TryFindControl<StackPanel>("MgmtGroupPanel");
            var fqdnPanel = TryFindControl<StackPanel>("FqdnPanel");
            var header1 = TryFindControl<TextBlock>("TargetResultsHeader1");
            var header2 = TryFindControl<TextBlock>("TargetResultsHeader2");

            var joinPanel = TryFindControl<StackPanel>("MgmtGroupJoinModePanel");
            var searchByRadiosPanel = TryFindControl<StackPanel>("SearchByRadiosPanel");

            bool isMgmtGroup = string.Equals(selectedMode, "Management Group", StringComparison.OrdinalIgnoreCase);
            bool isFqdn = string.Equals(selectedMode, "FQDN", StringComparison.OrdinalIgnoreCase);

            // MG dropdown row is not used in this UI (lower picker is used instead)
            if (mgPanel != null)
                mgPanel.IsVisible = false;

            // Shared lower picker area must be visible for BOTH FQDN and MG
            if (fqdnPanel != null)
                fqdnPanel.IsVisible = isFqdn || isMgmtGroup;

            // Show MG join toggle only in MG mode
            if (joinPanel != null)
                joinPanel.IsVisible = false;

            // Show Search-by radios only in FQDN mode
            if (searchByRadiosPanel != null)
                searchByRadiosPanel.IsVisible = isFqdn;

            // Clear target selections ONLY when switching between MG and FQDN
            if (_lastTargetingModeForClear == null ||
                !string.Equals(_lastTargetingModeForClear, selectedMode, StringComparison.OrdinalIgnoreCase))
            {
                bool wasMgmt = string.Equals(_lastTargetingModeForClear, "Management Group", StringComparison.OrdinalIgnoreCase);
                bool wasFqdn = string.Equals(_lastTargetingModeForClear, "FQDN", StringComparison.OrdinalIgnoreCase);

                if ((wasMgmt && isFqdn) || (wasFqdn && isMgmtGroup))
                {
                    _fqdnManagerSelectedItems?.Clear();

                    if (_fqdnTextBox != null)
                        _fqdnTextBox.Text = string.Empty;

                    if (_instructionFqdnSearchResultsListBox != null)
                        _instructionFqdnSearchResultsListBox.SelectedItem = null;

                    UpdateFqdnManagerSelectedHeader();
                }

                _lastTargetingModeForClear = selectedMode;
            }

            if (_fqdnTextBox != null)
                _fqdnTextBox.Watermark = isMgmtGroup
                    ? "Search management groups..."
                    : "Search by FQDN or Primary User...";

            if (header1 != null)
                header1.Text = isMgmtGroup ? "Management Group" : "FQDN";

            if (header2 != null)
                header2.Text = isMgmtGroup ? string.Empty : "Primary User";

            if (_instructionFqdnSearchResultsBorder != null)
                _instructionFqdnSearchResultsBorder.IsVisible = true;

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty);
                    await UpdatePreviewAsync();
                }
                catch
                {
                }
            });
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // -----------------------------
            // Required controls (must exist)
            // -----------------------------
            _logTextBox = this.FindControl<TextBox>("LogTextBox");
            _statusLabel = this.FindControl<TextBlock>("StatusLabel");
            _instructionListBox = this.FindControl<ListBox>("InstructionListBox");
            _searchBox = this.FindControl<TextBox>("SearchBox");

            _platformUrlDropdown = this.FindControl<ComboBox>("PlatformUrlDropdown");

            _fqdnTextBox = this.FindControl<TextBox>("FqdnTextBox");
            _fqdnManagerBorder = TryFindControl<Border>("FqdnManagerBorder");
            _fqdnManagerSearchTextBox = TryFindControl<TextBox>("FqdnManagerSearchTextBox");
            _fqdnManagerSearchByFqdnRadioButton = TryFindControl<RadioButton>("FqdnManagerSearchByFqdnRadioButton");
            _fqdnManagerSearchByUserRadioButton = TryFindControl<RadioButton>("FqdnManagerSearchByUserRadioButton");
            _fqdnManagerResultsGrid = TryFindControl<Avalonia.Controls.DataGrid>("FqdnManagerResultsGrid");
            _fqdnManagerSelectedListBox = TryFindControl<ListBox>("FqdnManagerSelectedListBox");
            _fqdnManagerStatusText = TryFindControl<TextBlock>("FqdnManagerStatusText");
            _fqdnManagerSelectedHeaderText = TryFindControl<TextBlock>("FqdnManagerSelectedHeaderText");
            if (_fqdnManagerSelectedListBox != null)
                _fqdnManagerSelectedListBox.ItemsSource = _fqdnManagerSelectedItems;

            // Instruction Detail FQDN picker (typeahead suggestions shown under the FQDN box)
            _instructionFqdnSearchResultsListBox = TryFindControl<ListBox>("InstructionFqdnSearchResults");
            _instructionFqdnSearchResultsBorder = TryFindControl<Border>("InstructionFqdnSearchResultsBorder");
            if (_instructionFqdnSearchResultsBorder != null)
                _instructionFqdnSearchResultsBorder.IsVisible = false;
            if (_instructionFqdnSearchResultsListBox != null)
                _instructionFqdnSearchResultsListBox.SelectionChanged += InstructionFqdnSearchResults_SelectionChanged;


            _runButton = this.FindControl<Button>("RunButton");
            _exportButton = this.FindControl<Button>("ExportButton");
            _exportAllButton = this.FindControl<Button>("ExportAllButton");

            // Results tab: server-side filtering toggle (optional)
            _resultsServerFilterAllCheckBox = TryFindControl<CheckBox>("ResultsServerFilterAllCheckBox");
            if (_resultsServerFilterAllCheckBox != null)
            {
                _resultsServerFilterAllCheckBox.Checked += async (_, __) =>
                {
                    try { await TriggerResultsServerRefilterAsync(); } catch { }
                };
                _resultsServerFilterAllCheckBox.Unchecked += async (_, __) =>
                {
                    try { await TriggerResultsServerRefilterAsync(); } catch { }
                };
            }

            // Settings tab: Default export format dropdown (used by Export + displayed in Results header)
            _exportFormatComboBox = TryFindControl<ComboBox>("DefaultExportFormatComboBox");
            if (_exportFormatComboBox != null)
            {
                _exportFormatComboBox.SelectionChanged += (_, __) =>
                {
                    try { UpdateDefaultExportFormatLabel(); } catch { }
                };
            }
            var mainTabControl = TryFindControl<TabControl>("MainTabControl");
            if (mainTabControl != null)
            {
                mainTabControl.SelectionChanged -= MainTabControl_SelectionChanged;
                mainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
            }

            _loginButton = this.FindControl<Button>("LoginButton");
            _logoutButton = this.FindControl<Button>("LogoutButton");

            _refreshHistoryButton = this.FindControl<Button>("RefreshHistoryButton");
            _rerunInstructionButton = this.FindControl<Button>("RerunInstructionButton");
            _cancelInstructionButton = this.FindControl<Button>("CancelInstructionButton");
            _resetFiltersButton = this.FindControl<Button>("ResetFiltersButton");

            _historyListBox = this.FindControl<ListBox>("HistoryListBox");
            _historyDatePicker = this.FindControl<DatePicker>("HistoryDatePicker");

            _progressSummaryPanel = this.FindControl<StackPanel>("ProgressSummaryPanel");
            _progressSummaryLabel = this.FindControl<TextBlock>("ProgressSummaryLabel");
            _progressCountsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");

            ExportProgressBar = this.FindControl<ProgressBar>("ExportProgressBar");
            ExportProgressLabel = this.FindControl<TextBlock>("ExportProgressLabel");
            ExportStatusPanel = this.FindControl<StackPanel>("ExportStatusPanel");

            _authStatusText = TryFindControl<TextBlock>("AuthStatusText");
            _tokenTimerText = TryFindControl<TextBlock>("TokenTimerText");

            // Paging controls are optional (won't crash if absent)
            _resultsPanel = TryFindControl<StackPanel>("ResultsPanel");
            _resultsPrevPageButton = TryFindControl<Button>("ResultsPrevPageButton");
            _resultsNextPageButton = TryFindControl<Button>("ResultsNextPageButton");
            _resultsPageInfoText = TryFindControl<TextBlock>("ResultsPageInfoText");
            _resultsPageSizeCombo = TryFindControl<ComboBox>("ResultsPageSizeCombo");

            // View radios must exist if Results view exists
            var rawRadio = TryFindControl<RadioButton>("RawViewRadioButton");
            var aggRadio = TryFindControl<RadioButton>("AggregatedViewRadioButton");
            var chartRadio = TryFindControl<RadioButton>("ChartViewRadioButton");

            // TTL UI optional
            _resultsTtlCountdownText = TryFindControl<TextBlock>("ResultsTtlCountdownText");
            _instructionTtlBox = TryFindControl<TextBox>("InstructionTtlBox");
            _responseTtlBox = TryFindControl<TextBox>("ResponseTtlBox");
            var responseUnitBox = TryFindControl<ComboBox>("ResponseTtlUnitDropdown");
            var instructionUnitBox = TryFindControl<ComboBox>("InstructionTtlUnitDropdown");

            if (_instructionTtlBox != null)
                _instructionTtlBox.GetObservable(TextBox.TextProperty).Subscribe(_ => ValidateTtlInputs());
            if (_responseTtlBox != null)
                _responseTtlBox.GetObservable(TextBox.TextProperty).Subscribe(_ => ValidateTtlInputs());

            if (instructionUnitBox != null)
            {
                instructionUnitBox.SelectionChanged += (_, __) => ValidateTtlInputs();
                if (instructionUnitBox.SelectedIndex < 0) instructionUnitBox.SelectedIndex = 0;
            }

            if (responseUnitBox != null)
            {
                responseUnitBox.SelectionChanged += (_, __) => ValidateTtlInputs();
                if (responseUnitBox.SelectedIndex < 0) responseUnitBox.SelectedIndex = 0;
            }



            _matchTypeComboBox = null; // never shown/used in this app

            // Targeting mode UI exists, but MG is hidden unless config enables it
            _targetingModeComboBox = this.FindControl<ComboBox>("TargetingModeComboBox");
            _managementGroupComboBox = this.FindControl<ComboBox>("ManagementGroupComboBox");

            _setDefaultMgButton = TryFindControl<Button>("SetDefaultMGButton");

            _configHelper = new ConfigHelper();
            _config = _configHelper.GetConfiguration();

            // Reflect config-driven defaults in the UI
            UpdateDefaultExportFormatLabel();

            _enableManagementGroupTargeting =
                bool.TryParse(_config["EnableManagementGroupTargeting"], out var enableMg) && enableMg;

            PopulateTargetingModeComboBox(); // MUST honor _enableManagementGroupTargeting

            // Default to FQDN always in helpdesk runner
            if (_targetingModeComboBox != null && _targetingModeComboBox.ItemCount > 0)
            {
                // Force-select FQDN
                for (int i = 0; i < _targetingModeComboBox.ItemCount; i++)
                {
                    var item = _targetingModeComboBox.Items[i];

                    string? content = item switch
                    {
                        ComboBoxItem cbi => cbi.Content?.ToString(),
                        string s => s,
                        _ => item?.ToString()
                    };

                    if (string.Equals(content?.Trim(), "FQDN", StringComparison.OrdinalIgnoreCase))
                    {
                        _targetingModeComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Hide MG controls entirely if not enabled in config
            if (!_enableManagementGroupTargeting)
            {
                if (_managementGroupComboBox != null) _managementGroupComboBox.IsVisible = false;
                if (_setDefaultMgButton != null) _setDefaultMgButton.IsVisible = false;
            }

            // ---------------------------------------------
            // Optional notification UI (do NOT assume exists)
            // ---------------------------------------------
            NotificationAlertButton = TryFindControl<Button>("NotificationAlertButton");
            NotificationPanel = TryFindControl<StackPanel>("NotificationPanel");
            NotificationList = TryFindControl<ListBox>("NotificationList");

            if (NotificationAlertButton != null) NotificationAlertButton.IsVisible = false;
            if (NotificationPanel != null) NotificationPanel.IsVisible = false;

            if (NotificationList != null)
                NotificationList.SelectionChanged += NotificationList_SelectionChanged;

            EnsureNotificationTimerInitialized();

            // ---------------------------------------------
            // Optional breakdown toggle (guard all usage)
            // ---------------------------------------------
            _breakdownModeToggle = TryFindControl<ToggleSwitch>("BreakdownModeToggle");
            _breakdownLabel = TryFindControl<TextBlock>("BreakdownLabel");

            if (_breakdownModeToggle != null)
            {
                _breakdownModeToggle.OnContent = "";
                _breakdownModeToggle.OffContent = "";
                _breakdownModeToggle.Content = null;

                _breakdownModeToggle.Checked += (_, __) =>
                {
                    if (_breakdownLabel != null) _breakdownLabel.Text = "📊 OS";
                    if (_lastPreviewResult != null) RenderBreakdownTables(_lastPreviewResult);
                };

                _breakdownModeToggle.Unchecked += (_, __) =>
                {
                    if (_breakdownLabel != null) _breakdownLabel.Text = "📊 Device Type";
                    if (_lastPreviewResult != null) RenderBreakdownTables(_lastPreviewResult);
                };

                if (_breakdownLabel != null)
                    _breakdownLabel.Text = _breakdownModeToggle.IsChecked == true ? "📊 OS" : "📊 Device Type";
            }

            // ---------------------------------------------
            // Wire core UI events (only those that exist now)
            // ---------------------------------------------
            this.Activated += (_, _) => ApplyThemeAwareLogBoxStyling();
            this.Deactivated += (_, _) => ApplyThemeAwareLogBoxStyling();

            _platformUrlDropdown.SelectionChanged += (_, __) => _ = UpdatePreviewAsync();

            if (_enableManagementGroupTargeting && _managementGroupComboBox != null)
                _managementGroupComboBox.SelectionChanged += (_, __) => _ = UpdatePreviewAsync();

            if (_targetingModeComboBox != null)
            {
                _targetingModeComboBox.SelectionChanged += (_, __) =>
                {
                    var mode = GetSelectedTargetingMode();
                    UpdateTargetingModePanels(mode);
                    _ = UpdatePreviewAsync();
                };

            }

            _fqdnTextBox.GetObservable(TextBox.TextProperty)
           .Subscribe(text =>
           {
               if (_suppressFqdnSuggest)
                   return;

               Dispatcher.UIThread.Post(async () =>
               {
                   await UpdateInstructionFqdnSearchResultsAsync(text ?? string.Empty);
                   await UpdatePreviewAsync();
               });
           });

            var searchByFqdnRadio = TryFindControl<RadioButton>("SearchByFqdnRadioButton");
            var searchByPrimaryUserRadio = TryFindControl<RadioButton>("SearchByPrimaryUserRadioButton");

            if (searchByFqdnRadio != null)
                searchByFqdnRadio.GetObservable(ToggleButton.IsCheckedProperty)
                    .Subscribe(_ => Dispatcher.UIThread.Post(async () => await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty)));

            if (searchByPrimaryUserRadio != null)
                searchByPrimaryUserRadio.GetObservable(ToggleButton.IsCheckedProperty)
                    .Subscribe(_ => Dispatcher.UIThread.Post(async () => await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty)));

            if (_runButton != null) _runButton.Click += OnRunClicked;
            if (_loginButton != null) _loginButton.Click += async (_, __) => await AuthenticateAsync();
            if (_logoutButton != null) _logoutButton.Click += (_, __) => _authService?.Logout();

            if (_refreshHistoryButton != null) _refreshHistoryButton.Click += async (_, __) => await LoadInstructionHistoryAsync();
            // Rerun is wired via XAML Click="RerunInstructionButton_Click" to avoid double-fire.
            if (_cancelInstructionButton != null) _cancelInstructionButton.Click += async (_, __) => await CancelInstructionAsync();
            if (_exportButton != null) _exportButton.Click += (_, __) => ExportResultsToFile();
            if (_exportAllButton != null) _exportAllButton.Click += async (_, __) => await ExportAllResultsToFileAsync();

            if (rawRadio != null) rawRadio.Checked += (_, __) => UpdateView("Raw");
            if (aggRadio != null) aggRadio.Checked += (_, __) => UpdateView("Aggregated");
            if (chartRadio != null) chartRadio.Checked += (_, __) => UpdateView("Chart");

            _instructionListBox.SelectionChanged += InstructionListBox_SelectionChanged;
            _searchBox.GetObservable(TextBox.TextProperty).Subscribe(new AnonymousObserver<string?>(UpdateInstructionList));

            _historyListBox.DoubleTapped += HistoryListBox_DoubleTapped;
            _historyListBox.SelectionChanged += HistoryListBox_SelectionChanged;
            _historyDatePicker.SelectedDate = DateTimeOffset.Now;

            // Paging wiring (optional)
            if (_resultsPrevPageButton != null)
            {
                _resultsPrevPageButton.Click -= ResultsPrevPageButton_Click;
                _resultsPrevPageButton.Click += ResultsPrevPageButton_Click;
            }
            if (_resultsNextPageButton != null)
            {
                _resultsNextPageButton.Click -= ResultsNextPageButton_Click;
                _resultsNextPageButton.Click += ResultsNextPageButton_Click;
            }
            if (_resultsPageSizeCombo != null)
            {
                _resultsPageSizeCombo.SelectionChanged -= ResultsPageSizeCombo_SelectionChanged;
                _resultsPageSizeCombo.SelectionChanged += ResultsPageSizeCombo_SelectionChanged;
            }

            UpdateResultsPagingUi();

            // ---------------------------------------------
            // Auth services (create ONCE, after config)
            // ---------------------------------------------
            _tokenStoreService = new TokenStoreService(_logTextBox);

            _authService = new AuthenticationService(
                _configHelper,
                _logTextBox,
                _experienceService,
                _tokenStoreService,
                _selectedPlatform?.Consumer ?? "Explorer",
                this);

            this.Loaded += MainWindow_Loaded;

            _authService.TokenTimeRemainingUpdated += time =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (string.Equals(time, "Expired", StringComparison.OrdinalIgnoreCase))
                    {
                        try { UpdateAuthStatusIndicator(false); } catch { }
                        if (_tokenTimerText != null) _tokenTimerText.Text = string.Empty;
                        return;
                    }

                    if (_authStatusText != null) _authStatusText.Text = $"Authenticated – {time} remaining";
                    if (_tokenTimerText != null) _tokenTimerText.Text = $"🕒 {time}";
                });
            };

            _authService.AuthStatusChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { _token = _authService?.Token; } catch { }
                    OnAuthStatusChanged(string.IsNullOrWhiteSpace(_authService?.Token) ? "NotAuthenticated" : "Authenticated");
                    UpdateAuthStatusIndicator(!string.IsNullOrWhiteSpace(_authService?.Token));
                });
            };

            StartNotificationTimerIfAuthenticated();

            // Kick initial platform load
            _ = InitializePlatformAsync();

            // Initialize auth UI default
            if (_authStatusText != null)
            {
                _authStatusText.Text = "Unauthenticated";
                _authStatusText.Foreground = Brushes.Red;
            }

            // Show log toggle (optional)
            var showLogToggle = TryFindControl<ToggleSwitch>("ShowLogToggle");
            var logSection = TryFindControl<StackPanel>("LogSection");
            if (showLogToggle != null && logSection != null)
            {
                bool showLogFromConfig = false;
                if (bool.TryParse(_config["ShowLogOnStartup"], out bool parsedValue))
                    showLogFromConfig = parsedValue;

                showLogToggle.IsChecked = showLogFromConfig;
                logSection.IsVisible = showLogFromConfig;
                showLogToggle.Checked += (_, __) => logSection.IsVisible = true;
                showLogToggle.Unchecked += (_, __) => logSection.IsVisible = false;
            }
        }

        private string GetSelectedTargetingMode()
        {
            if (_targetingModeComboBox == null)
                return string.Empty;

            var selected = _targetingModeComboBox.SelectedItem;
            if (selected == null)
                return string.Empty;

            // ComboBoxItem case (most common in your UI)
            if (selected is ComboBoxItem cbi)
                return (cbi.Content?.ToString() ?? string.Empty).Trim();

            // Plain string case
            if (selected is string s)
                return s.Trim();

            // Fallback
            return (selected.ToString() ?? string.Empty).Trim();
        }


        private async Task FinalizeLoginAsync()
        {
            await AuthenticateAsync();
            DumpJwtPayload(_token);
            await LoadCustomPropertiesAsync();
            await CheckNotificationsAsync();
            //await PreloadExperienceFiltersAsync();
            ApplyThemeAwareLogBoxStyling();
            _experienceService = new ExperienceService(_selectedPlatform.Url, _token, _logTextBox);
            _experienceMeasures = await _measureService.GetExperienceMeasuresAsync(_selectedPlatform.Url, _token);


            LogToUI($"📊 Loaded {_experienceMeasures.Count} measures.");

            //PopulateMeasuresListBox();

            // await LoadExperienceFiltersAsync();

            UpdateAuthStatusIndicator(!string.IsNullOrEmpty(_token));
        }

        private async void OnRefreshTokenClicked(object sender, RoutedEventArgs e)
        {
            LogToUI("🔄 Refresh Token button clicked.");

            bool success = await _authService.RefreshTokenAsync();

            if (success)
            {
                LogToUI("✅ Token refresh successful.");
            }
            else
            {
                LogToUI("❌ Token refresh failed.");
            }
        }
        private void NotificationList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (NotificationList.SelectedItem is not ApprovalInstruction selected)
                return;

            // Reset selection state
            foreach (ApprovalInstruction item in NotificationList.Items)
                item.IsSelected = false;

            // Mark the selected one
            selected.IsSelected = true;
            SelectedApproval = selected;

            // 🔍 Debug Logging
            LogToUI($"📌 Selected Approval:");
            LogToUI($"   - Id (approval object): {selected.Id}");
            LogToUI($"   - InstructionId (definition): {selected.InstructionId}");
            LogToUI($"   - Name: {selected.InstructionName}");
            LogToUI($"   - Targeting: {selected.TargetingPercentDisplay}");
        }




        private async void OnRejectClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ApprovalInstruction instruction)
            {
                LogToUI($"❌ Rejected: {instruction.InstructionName}");

                var body = new
                {
                    type = "Instruction",
                    id = instruction.Id,  // ✅ Approval object ID
                    approved = false,
                    comment = instruction.Comment ?? "",
                    InstructionId = instruction.Id,  // ✅ Underlying instruction definition ID
                    Approved = false,
                    Comment = instruction.Comment ?? ""
                };
                LogToUI($"🔍 Sending Rejection for ID: {instruction.Id}, Comment: {instruction.Comment}");
                await SendApprovalAsync(body);
            }
        }

        private async void OnApproveClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ApprovalInstruction instruction)
            {
                LogToUI($"✅ Approved: {instruction.InstructionName}");

                var body = new
                {
                    type = "Instruction",
                    id = instruction.Id,  // ✅ Approval object ID
                    approved = true,
                    comment = instruction.Comment ?? "",
                    InstructionId = instruction.Id,  // ✅ Underlying instruction definition ID
                    Approved = true,
                    Comment = instruction.Comment ?? ""
                };
                LogToUI($"🔍 Sending approval for ID: {instruction.Id}, Comment: {instruction.Comment}");
                await SendApprovalAsync(body);
                await CheckNotificationsAsync();
            }
        }


        private async Task SendApprovalAsync(object body)
        {
            if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_selectedPlatform.Url))
            {
                LogToUI("❌ Cannot send approval. Missing token or platform URL.");
                return;
            }

            try
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);


                string url = $"https://{_selectedPlatform.Url}/consumer/Approvals/Instruction";

                var json = JsonConvert.SerializeObject(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    //LogToUI($"✅ API response: {result}");
                }
                else
                {
                    LogToUI($"❌ API failed ({response.StatusCode}): {result}");
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Exception during approval API call: {ex.Message}");
            }
        }


        private ApprovalInstruction _selectedApproval;
        public ApprovalInstruction SelectedApproval
        {
            get => _selectedApproval;
            set
            {
                if (_selectedApproval != value)
                {
                    _selectedApproval = value;
                    OnPropertyChanged(nameof(SelectedApproval));
                }
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RepopulatePlatformDropdownPreserveSelection(List<string> urls)
        {
            var previous = _platformUrlDropdown?.SelectedItem as string;

            _suppressPlatformSelectionChanged = true;
            try
            {
                _platformUrlDropdown.ItemsSource = urls;

                if (!string.IsNullOrWhiteSpace(previous) && urls.Any(u => string.Equals(u, previous, StringComparison.OrdinalIgnoreCase)))
                {
                    _platformUrlDropdown.SelectedItem = urls.First(u => string.Equals(u, previous, StringComparison.OrdinalIgnoreCase));
                }
                else if (urls.Count > 0)
                {
                    // Fall back only if the previously selected item is gone
                    _platformUrlDropdown.SelectedIndex = 0;
                }
            }
            finally
            {
                _suppressPlatformSelectionChanged = false;
            }
        }

        private void EnsureNotificationTimerInitialized()
        {
            if (_notificationTimer == null)
            {
                _notificationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(1)
                };
            }
            else
            {
                _notificationTimer.Interval = TimeSpan.FromMinutes(1);
            }

            if (!_notificationTimerWired)
            {
                _notificationTimer.Tick += NotificationTimer_Tick;
                _notificationTimerWired = true;
            }
        }

        private async void NotificationTimer_Tick(object? sender, EventArgs e)
        {
            await CheckNotificationsAsync();
        }

        private void StartNotificationTimerIfAuthenticated()
        {
            EnsureNotificationTimerInitialized();
            if (string.IsNullOrWhiteSpace(_token))
            {
                StopNotificationTimer();
                return;
            }

            if (!_notificationTimer.IsEnabled)
                _notificationTimer.Start();
        }

        private void StopNotificationTimer()
        {
            try
            {
                if (_notificationTimer != null && _notificationTimer.IsEnabled)
                    _notificationTimer.Stop();
            }
            catch
            {
                // ignore
            }
        }

        private void ForceUnauthenticatedState(string reason)
        {
            try
            {
                StopNotificationTimer();
            }
            catch { }

            try
            {
                // Stop Results live polling immediately.
                _activeResultsInstructionId = -1;
                _resultsStatusPollRunId++;
            }
            catch { }

            try
            {
                _token = null;
            }
            catch { }

            try
            {
                // Prefer local cleanup (no network) when we already know auth is invalid.
                _authService?.Logout();
            }
            catch { }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    UpdateAuthStatusIndicator(false);
                }
                catch { }

                try
                {
                    if (_tokenTimerText != null)
                        _tokenTimerText.Text = string.Empty;
                }
                catch { }

                try
                {
                    LogToUI($"❌ Unauthorized – forcing logout: {reason}");
                }
                catch { }

                try
                {
                    if (!_authExpiredDialogShown)
                    {
                        _authExpiredDialogShown = true;
                        _ = ShowSimpleDialogAsync("Authentication", "Your session has expired. Please reauthenticate.");
                    }
                }
                catch (Exception ex)
                {
                    try { LogToUI($"⚠️ Expired-session dialog failed: {ex.Message}"); } catch { }
                }

            });
        }


        private async Task CheckNotificationsAsync()
        {
            // If we are not authenticated, do not poll.
            if (string.IsNullOrWhiteSpace(_token) || _selectedPlatform == null)
            {
                StopNotificationTimer();
                return;
            }

            try
            {
                string url = $"https://{_selectedPlatform.Url}/Consumer/Approvals/notifications";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform.Consumer ?? "Explorer");

                var response = await client.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    ForceUnauthenticatedState("Approvals notifications returned 401");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();

                // Some platforms return a root object with an "Instructions" array; others may return an array directly.
                JToken parsed;
                try
                {
                    parsed = JToken.Parse(json);
                }
                catch (Exception ex)
                {
                    LogToUI($"❌ Notification check failed to parse JSON: {ex.Message}");
                    return;
                }

                JArray instructionsArray = null;
                if (parsed.Type == JTokenType.Array)
                {
                    instructionsArray = (JArray)parsed;
                }
                else if (parsed.Type == JTokenType.Object)
                {
                    instructionsArray = parsed["Instructions"] as JArray;
                }

                var instructions = instructionsArray?.ToObject<List<JObject>>();

                if (instructions == null || instructions.Count == 0)
                {
                    NotificationAlertButton.IsVisible = false;
                    NotificationPanel.IsVisible = false;
                    //  LogToUI("✅ No pending approvals found.");
                    return;
                }
                var list = new List<ApprovalInstruction>();

                _pendingApprovals = list;


                NotificationAlertButton.Content = $"🔔 ({instructions.Count})";
                NotificationAlertButton.IsVisible = true;
                NotificationAlertButton.Background = Brushes.Orange;

                // LogToUI($"📢 You have {instructions.Count} pending approval notification(s).");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Notification check failed: {ex.Message}");
            }
        }

        private async void OnNotificationAlertClicked(object sender, RoutedEventArgs e)
        {
            if (NotificationPanel.IsVisible)
            {
                NotificationPanel.IsVisible = false;
                return;
            }

            try
            {
                //LogToUI("🔔 Notification icon clicked.");

                if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_token))
                {
                    LogToUI("⚠️ Cannot load approvals — missing token or platform.");
                    return;
                }

                string url = $"https://{_selectedPlatform.Url}/Consumer/Approvals/notifications";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

                var response = await client.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    ForceUnauthenticatedState("Approvals notifications returned 401");
                    return;
                }
                var json = await response.Content.ReadAsStringAsync();

                JToken parsed;
                try
                {
                    parsed = JToken.Parse(json);
                }
                catch (Exception ex)
                {
                    LogToUI($"❌ Failed to parse approvals JSON: {ex.Message}");
                    NotificationPanel.IsVisible = false;
                    return;
                }

                JArray instructions = null;
                if (parsed.Type == JTokenType.Array)
                    instructions = (JArray)parsed;
                else if (parsed.Type == JTokenType.Object)
                    instructions = parsed["Instructions"] as JArray;

                if (instructions == null || !instructions.Any())
                {
                    LogToUI("📭 No pending approvals found.");
                    NotificationPanel.IsVisible = false;
                    return;
                }

                var list = new List<ApprovalInstruction>();

                foreach (var inst in instructions)
                {
                    try
                    {
                        var id = inst.Value<int?>("Id") ?? 0;
                        var name = inst.Value<string>("Name") ?? "";
                        var createdBy = inst.Value<string>("CreatedBy") ?? "";
                        var createdUtc = inst.Value<DateTime?>("CreatedTimestampUtc")?.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt") ?? "";

                        var (online, offline, targeted, totalOnline, percent) = await FetchTargetingImpactAsync(id);

                        list.Add(new ApprovalInstruction
                        {
                            Id = id,
                            InstructionName = name,
                            CreatedTimestampUtc = createdUtc,
                            TargetingPercent = percent,
                            TargetedOnline = online,
                            TargetedDevices = targeted,
                            TotalOnline = totalOnline
                        });
                    }
                    catch (Exception innerEx)
                    {
                        LogToUI($"⚠️ Error processing instruction: {innerEx.Message}");
                        TryLogCrash($"[Instruction Parse Error] {DateTime.Now}\n{innerEx}\n");
                    }
                }


                _pendingApprovals = list;
                NotificationList.ItemsSource = _pendingApprovals;
                NotificationPanel.IsVisible = true;

                LogToUI($"📢 Displaying {_pendingApprovals.Count} approval(s).");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load approvals: {ex.Message}");
                TryLogCrash($"[Notification Crash] {DateTime.Now}\n{ex}\n");
                NotificationPanel.IsVisible = false;
            }

            // Automatically switch to the History tab
            try
            {
                var tabControl = this.FindControl<TabControl>("MainTabControl");
                if (tabControl != null)
                {
                    foreach (var tab in tabControl.Items)
                    {
                        if (tab is TabItem tabItem && tabItem.Header?.ToString().Contains("History", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            tabControl.SelectedItem = tabItem;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Failed to switch to History tab: {ex.Message}");
                TryLogCrash($"[Tab Switch Error] {DateTime.Now}\n{ex}\n");
            }
        }
        private void TryLogCrash(string logEntry)
        {
            try
            {
                File.AppendAllText("crash.log", logEntry + "\n");
            }
            catch
            {
                // fail silently
            }
        }



        private async Task<(int online, int offline, int totalTargeted, int totalOnline, double percentTargeted)> FetchTargetingImpactAsync(int instructionId)
        {
            try
            {
                if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_token))
                {
                    LogToUI("⚠️ Missing platform or token.");
                    return (0, 0, 0, 0, 0);
                }

                // 1. Fetch targeting estimate for the instruction
                string approxUrl = $"https://{_selectedPlatform.Url}/consumer/instructions/{instructionId}/approxtarget";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

                var approxResponse = await client.GetAsync(approxUrl);
                var approxJson = await approxResponse.Content.ReadAsStringAsync();

                if (!approxResponse.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Failed to fetch targeting estimate: {approxResponse.StatusCode}");
                    return (0, 0, 0, 0, 0);
                }

                var approxResult = JObject.Parse(approxJson);
                int online = approxResult["TotalDevicesOnline"]?.Value<int>() ?? 0;
                int offline = approxResult["TotalDevicesOffline"]?.Value<int>() ?? 0;
                int totalTargeted = online + offline;

                // 2. Fetch total connected devices from highlevel
                string statsUrl = $"https://{_selectedPlatform.Url}/consumer/systemstatistics/highlevel";
                var statsResponse = await client.GetAsync(statsUrl);
                var statsJson = await statsResponse.Content.ReadAsStringAsync();

                if (!statsResponse.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Failed to fetch platform stats: {statsResponse.StatusCode}");
                    return (online, offline, totalTargeted, 0, 0);
                }

                var statsResult = JObject.Parse(statsJson);
                int totalOnline = statsResult["ConnectedDeviceCount"]?.Value<int>() ?? 0;

                // 3. Calculate percentage
                double rawPercent = (totalOnline > 0) ? (totalTargeted * 100.0) / totalOnline : 0;
                double percent = Math.Min(Math.Round(rawPercent, 1), 100);


                return (online, offline, totalTargeted, totalOnline, Math.Round(percent, 1));
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Exception in FetchTargetingImpactAsync: {ex.Message}");
                return (0, 0, 0, 0, 0);
            }
        }

        private async Task LoadPendingApprovalsAsync()
        {
            if (string.IsNullOrEmpty(_token) || _selectedPlatform == null)
            {
                LogToUI("❌ Cannot load approvals. Token or platform is missing.");
                return;
            }

            string url = $"https://{_selectedPlatform.Url}/Consumer/Approvals/notifications";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

            try
            {
                var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                var root = JObject.Parse(json);
                var instructions = root["Instructions"] as JArray;
                if (instructions == null || instructions.Count == 0)
                {
                    _pendingApprovals = new List<ApprovalInstruction>();
                    LogToUI("ℹ️ No pending approvals found.");
                    return;
                }

                var list = new List<ApprovalInstruction>();

                foreach (var inst in instructions)
                {
                    var id = inst.Value<int>("Id");
                    var name = inst.Value<string>("Name") ?? "Unknown";
                    var created = inst.Value<DateTime?>("CreatedTimestampUtc")?.ToLocalTime().ToString("g") ?? "";

                    var (online, offline, targeted, totalOnline, percentTargeted) = await FetchTargetingImpactAsync(id);

                    list.Add(new ApprovalInstruction
                    {
                        Id = id,
                        InstructionName = name,
                        CreatedTimestampUtc = created,
                        TargetingPercent = percentTargeted
                    });
                }

                _pendingApprovals = list;
                LogToUI($"📥 Loaded {_pendingApprovals.Count} pending approval(s).");
            }
            catch (Exception ex)
            {
                LogToUI("❌ Error loading approvals: " + ex.Message);
            }
        }



        private void UpdateOperatorsAndValues()
        {
            string selectedAttr = _filterAttributeComboBox.SelectedItem?.ToString() ?? "";
            var attr = DynamicTargetingHelper.GetAttributes().FirstOrDefault(a => a.Name == selectedAttr);

            if (attr != null)
            {
                _filterOperatorComboBox.ItemsSource = new List<string> { "=", "!=", "Contains", "Begins with", "Ends with" };
                _filterOperatorComboBox.SelectedIndex = 0;

                if (attr.AllowedValues != null && attr.AllowedValues.Any())
                {
                    _filterValueComboBox.ItemsSource = attr.AllowedValues;
                    _filterValueComboBox.SelectedIndex = 0;
                    _filterValueComboBox.IsVisible = true;
                    _filterValueTextBox.IsVisible = false;
                }
                else
                {
                    _filterValueComboBox.IsVisible = false;
                    _filterValueTextBox.IsVisible = true;
                    _filterValueTextBox.Text = "";
                }
            }
        }
        /*
         private void MeasuresListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
          {
              if (MeasureCheckboxList.ItemsSource is ListBoxItem item && item.Tag is ExperienceMeasure measure)
              {
                  if (MeasureNotesTextBlock != null)
                  {
                      MeasureNotesTextBlock.Text = measure.Notes ?? "";
                  }

              }
              else
              {
                  MeasureNotesTextBlock.Text = "";
              }

          }
        */

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //  await LoadExperienceFiltersAsync();
            await EnsurePlatformConfiguredOrPromptAsync();


        }
        /*
                private void MeasureSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
                {
                    if (_experienceMeasures == null) return;

                    string searchTerm = MeasureSearchBox.Text?.Trim().ToLowerInvariant() ?? "";

                    var filtered = _experienceMeasures
                        .Where(m => !m.Hidden && m.Title.ToLowerInvariant().Contains(searchTerm))
                        .OrderBy(m => m.Title)
                        .Select(m => new SelectableMeasure
                        {
                            Measure = m,
                            IsSelected = _selectedMeasureTitles.Contains(m.Title)
                        }).ToList();

                    MeasureCheckboxList.ItemsSource = filtered;
                    LogToUI($"🔍 Filtered measures: {filtered.Count} match \"{searchTerm}\"");
                }
        */
        private void OpenSettingsFlyout()
        {
            try
            {
                var border = this.FindControl<Border>("SettingsFlyoutBorder");
                if (border == null)
                    return;

                // Force open (do not toggle)
                border.IsVisible = true;

                LoadSettingsFlyoutFromConfig();
                LoadPlatformsIntoSettingsFlyout();
            }
            catch
            {
                // ignore
            }
        }

        private bool _platformPromptShown;
        private const string DefaultConsumerName = "Explorer";

        private Task EnsurePlatformConfiguredOrPromptAsync()
        {
            if (_platformPromptShown)
                return Task.CompletedTask;

            _platformPromptShown = true;

            string? firstUrl = TryGetFirstConfiguredPlatformUrl(_config);

            if (!string.IsNullOrWhiteSpace(firstUrl))
                return Task.CompletedTask;

            try
            {
                LogToUI("⚠️ No platform configured. Please add a platform in Settings.\n");
            }
            catch { }

            // Open the Settings flyout (force open, not toggle)
            OpenSettingsFlyout();

            return Task.CompletedTask;
        }


        private static string? TryGetFirstConfiguredPlatformUrl(IConfiguration? config)
        {
            if (config == null)
                return null;

            try
            {
                var encrypted = config.GetSection("AuthenticationConfig:EncryptedPlatformUrls");
                if (encrypted.Exists())
                {
                    foreach (var child in encrypted.GetChildren())
                    {
                        var alias = child["Alias"];
                        var urlEnc = child["UrlEnc"];
                        var url = ConfigHelper.NormalizePlatformUrl(PlatformUrlProtector.DecryptPlatformUrl(alias, urlEnc));
                        if (!string.IsNullOrWhiteSpace(url))
                            return url;
                    }
                }

                var legacy = config.GetSection("AuthenticationConfig:PlatformUrls");
                if (legacy.Exists())
                {
                    foreach (var child in legacy.GetChildren())
                    {
                        var url = ConfigHelper.NormalizePlatformUrl(child["Url"]);
                        if (!string.IsNullOrWhiteSpace(url))
                            return url;
                    }
                }

                var single = config["PlatformUrl"];
                if (!string.IsNullOrWhiteSpace(single))
                    return ConfigHelper.NormalizePlatformUrl(single);
            }
            catch { }

            return null;
        }

        private async Task<string?> ShowPlatformFqdnPromptAsync()
        {
            var tcs = new TaskCompletionSource<string?>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var input = new TextBox
                {
                    Watermark = "*.cloud.1e.com",
                    MinWidth = 420
                };


                var okBtn = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
                var cancelBtn = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancelBtn, okBtn }
                };

                var panel = new StackPanel
                {
                    Spacing = 12,
                    Margin = new Thickness(16),
                    Children =
            {
                new TextBlock
                {
                    Text = "No platform is configured.\n\nEnter the platform FQDN to continue:",
                    TextWrapping = TextWrapping.Wrap
                },
                input,
                buttons
            }
                };

                var dlg = new Window
                {
                    Title = "Platform Required",
                    Width = 520,
                    Height = 220,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = panel
                };

                dlg.Closed += (_, __) =>
                {
                    if (!tcs.Task.IsCompleted)
                        tcs.TrySetResult(null);
                };

                okBtn.Click += (_, __) =>
                {
                    var value = input.Text?.Trim();
                    dlg.Close();
                    tcs.TrySetResult(value);
                };

                cancelBtn.Click += (_, __) =>
                {
                    dlg.Close();
                    tcs.TrySetResult(null);
                };

                dlg.ShowDialog(this);
                input.Focus();
            });

            return await tcs.Task;
        }

        private static void EnsureAppSettingsHasPlatform(string platformFqdn)
        {
            if (string.IsNullOrWhiteSpace(platformFqdn))
                return;

            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            JObject root;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                root = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
            }
            else
            {
                root = new JObject();
            }

            var auth = (JObject?)root["AuthenticationConfig"] ?? new JObject();
            root["AuthenticationConfig"] = auth;

            var arr = (JArray?)auth["EncryptedPlatformUrls"] ?? new JArray();
            auth["EncryptedPlatformUrls"] = arr;
            auth.Remove("PlatformUrls");

            var normalizedPlatform = ConfigHelper.NormalizePlatformUrl(platformFqdn.Trim());
            var derivedAlias = ConfigHelper.DeriveAliasFromHost(normalizedPlatform);
            bool already = arr
                .OfType<JObject>()
                .Any(o => string.Equals(ConfigHelper.NormalizePlatformUrl(PlatformUrlProtector.DecryptPlatformUrl(o["Alias"]?.ToString(), o["UrlEnc"]?.ToString())), normalizedPlatform, StringComparison.OrdinalIgnoreCase));

            if (!already)
            {
                arr.Add(new JObject
                {
                    ["Alias"] = derivedAlias,
                    ["UrlEnc"] = PlatformUrlProtector.EncryptPlatformUrl(derivedAlias, normalizedPlatform),
                    ["DefaultMG"] = "prod",
                    ["Consumer"] = DefaultConsumerName
                });

                if (string.IsNullOrWhiteSpace(auth["DefaultPlatformAlias"]?.ToString()))
                    auth["DefaultPlatformAlias"] = derivedAlias;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
        }


        public class SelectableMeasure : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isSelected;

            public ExperienceMeasure Measure { get; set; } = null!;

            // Safely access Title and Notes from Measure
            public string Title => Measure?.Title ?? "No Title";
            public string Measures => string.Join(", ", Measure.Measures ?? new List<string>());

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }
        /*
                private async void SelectAllMeasuresCheckbox_Checked(object? sender, RoutedEventArgs e)
                {
                    if (MeasureCheckboxList.ItemsSource is not IEnumerable<SelectableMeasure> measures)
                        return;

                    // Selecting hundreds of items can flood UI notifications and make the app appear hung.
                    // Apply selection in small batches and yield to the UI thread between batches.
                    var list = measures as IList<SelectableMeasure> ?? measures.ToList();
                    const int batchSize = 25;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var m = list[i];
                        m.IsSelected = true;

                        if (!string.IsNullOrWhiteSpace(m.Measure?.Title))
                            _selectedMeasureTitles.Add(m.Measure.Title);

                        if ((i + 1) % batchSize == 0)
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                }


                private async void SelectAllMeasuresCheckbox_Unchecked(object? sender, RoutedEventArgs e)
                {
                    if (MeasureCheckboxList.ItemsSource is not IEnumerable<SelectableMeasure> measures)
                        return;

                    var list = measures as IList<SelectableMeasure> ?? measures.ToList();
                    const int batchSize = 25;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var m = list[i];
                        m.IsSelected = false;

                        if (!string.IsNullOrWhiteSpace(m.Measure?.Title))
                            _selectedMeasureTitles.Remove(m.Measure.Title);

                        if ((i + 1) % batchSize == 0)
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                }

                private void AddFilterGroup_Click(object? sender, RoutedEventArgs e)
                {
                    // logic for adding a new filter group
                }
                private Border BuildFilterChip(FilterExpression filter, Action onRemove)
                {
                    var text = new TextBlock
                    {
                        Text = $"{filter.Attribute} {filter.Operator} {filter.Value}",
                        Foreground = Brushes.Black, // Changed from White to Black
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0),
                        FontWeight = FontWeight.SemiBold
                    };

                    var close = new Button
                    {
                        Content = "❌",
                        Foreground = Brushes.HotPink,
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        Padding = new Thickness(2, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    close.Click += (_, __) => onRemove();

                    var stack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { text, close }
                    };

                    return new Border
                    {
                        Background = Brushes.LightGray, // Light chip background
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 2),
                        Child = stack
                    };
                }

                private async void RefreshFiltersButton_Click(object? sender, RoutedEventArgs e)
                {
                    if (ExperienceLoadingLabel != null)
                        ExperienceLoadingLabel.IsVisible = true;


                    _experienceMeasures = await _measureService.GetExperienceMeasuresAsync(_selectedPlatform.Url, _token);

                    PopulateMeasuresListBox();
                    await LoadExperienceFiltersAsync(true);  // forceReload = true ensures fresh fetch

                    if (ExperienceLoadingLabel != null)
                        ExperienceLoadingLabel.IsVisible = false;
                }

                private async void FetchExperienceFromFqdnButton_Click(object sender, RoutedEventArgs e)
                {
                    try
                    {
                        // Ensure platformUrl and token are passed to FetchExperienceScoresAsync
                        string platformUrl = _selectedPlatform?.Url; // Ensure this is set correctly
                        string token = _token; // Ensure token is available

                        // Check if platformUrl or token is missing and handle it
                        if (string.IsNullOrEmpty(platformUrl) || string.IsNullOrEmpty(token))
                        {
                            LogToUI("❌ Platform URL or token is missing. Cannot fetch experience scores.", "error");
                            return; // Exit early if platformUrl or token is missing
                        }

                        // Now call FetchExperienceScoresAsync and pass platformUrl and token
                        await FetchExperienceScoresAsync(null, platformUrl, token);

                        await Task.Delay(100); // 🔥 Small delay helps ensure Experience tab UI is ready

                        // 🛡️ Safer check before switching tab
                        if (MainTabControl != null && MainTabControl.ItemCount > 4)
                        {
                            MainTabControl.SelectedIndex = 4;
                            LogToUI("✅ Switched to Experience tab after fetching scores.", "debug");
                        }
                        else
                        {
                            LogToUI("⚠️ MainTabControl or Experience tab not ready.", "debug");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Error fetching experience scores: {ex.Message}", "error");
                    }
                }
        */


        internal void LogToUI(string message, string level = "Info")
        {
            message = RedactPlatformUrlsToAlias(message);
            message = (message ?? string.Empty).TrimEnd('\r', '\n');
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_logTextBox != null)
                {
                    if (level.Equals("Debug", StringComparison.OrdinalIgnoreCase) && _logLevel != "Debug")
                        return; // Skip debug logs unless in Debug mode

                    _logTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
                    _logTextBox.CaretIndex = _logTextBox.Text?.Length ?? 0;
                }
            });

            // Only write to the log file for info and above
            if (level != "Debug")
            {
                WriteToLogFile($"[{level}] {message}");
            }
        }

        private string RedactPlatformUrlsToAlias(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            try
            {
                // Prefer alias replacement (easier for troubleshooting than *********)
                // Use any in-memory platform lists we have.
                var pairs = new List<(string url, string alias)>();

                if (_platformConfigs != null)
                {
                    foreach (var p in _platformConfigs)
                    {
                        if (p == null)
                            continue;

                        var url = (p.Url ?? string.Empty).Trim();
                        var alias = (p.Alias ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(url))
                            continue;
                        if (string.IsNullOrWhiteSpace(alias))
                        {
                            alias = url;
                            var dot = alias.IndexOf('.');
                            if (dot > 0)
                                alias = alias.Substring(0, dot);
                        }

                        url = NormalizePlatformUrlForUi(url);
                        pairs.Add((url, alias));
                    }
                }

                // Settings flyout list has plaintext urls and aliases too.
                if (_settingsPlatformRows != null)
                {
                    foreach (var r in _settingsPlatformRows)
                    {
                        if (r == null || r.IsNewRow)
                            continue;

                        var url = NormalizePlatformUrlForUi(r.UrlPlain);
                        if (string.IsNullOrWhiteSpace(url))
                            continue;

                        var alias = (r.Alias ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(alias))
                        {
                            alias = url;
                            var dot = alias.IndexOf('.');
                            if (dot > 0)
                                alias = alias.Substring(0, dot);
                        }

                        pairs.Add((url, alias));
                    }
                }

                // Dedupe by url.
                pairs = pairs
                    .GroupBy(p => p.url, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                foreach (var (url, alias) in pairs)
                {
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    var token = $"[{alias}]";
                    message = message.Replace($"https://{url}", token, StringComparison.OrdinalIgnoreCase);
                    message = message.Replace($"http://{url}", token, StringComparison.OrdinalIgnoreCase);
                    message = message.Replace(url, token, StringComparison.OrdinalIgnoreCase);
                    message = message.Replace($"[{url}]", token, StringComparison.OrdinalIgnoreCase);
                    message = message.Replace($"[https://{url}]", token, StringComparison.OrdinalIgnoreCase);
                    message = message.Replace($"[http://{url}]", token, StringComparison.OrdinalIgnoreCase);

                }


                // Fallback: if we haven't loaded an alias map yet, still redact common platform FQDNs to [firstLabel]
                // so the UI log never leaks full platform hostnames.
                try
                {
                    message = System.Text.RegularExpressions.Regex.Replace(
                        message,
                        @"\b([a-zA-Z0-9-]+)\.([a-zA-Z0-9-]+\.)*cloud\.[a-zA-Z0-9-]+\.[a-zA-Z]{2,}\b",
                        m => $"[{m.Groups[1].Value}]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                catch
                {
                    // ignore
                }

            }
            catch
            {
                // ignore redaction failures
            }

            return message;
        }


        private async void SetDefaultMGButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedMG = _managementGroupComboBox?.SelectedItem?.ToString();
                var selectedPlatform = _selectedPlatform;

                if (string.IsNullOrEmpty(selectedMG) || selectedPlatform == null)
                {
                    LogToUI("⚠️ Cannot set default MG - missing selection.", "Warning");
                    return;
                }

                if (_configHelper?.UpdateDefaultManagementGroup(selectedPlatform.Url, selectedMG) == true)
                    LogToUI($"✅ Updated default MG to '{selectedMG}' for platform alias '{selectedPlatform.Alias ?? selectedPlatform.Url}'", "Info");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to update default MG: {ex.Message}", "Error");
            }
        }

        private async Task FlashManagementGroupComboBoxAsync()
        {
            try
            {
                var originalBrush = _managementGroupComboBox.Background;
                _managementGroupComboBox.Background = new SolidColorBrush(Colors.LightGreen);
                await Task.Delay(700);
                _managementGroupComboBox.Background = originalBrush;
            }
            catch
            {
                // Ignore flashing issues
            }
        }


        /*

        private async Task FetchExperienceScoresAsync(List<string> overrideFqdns = null, string platformUrl = null, string token = null)
        {
            try
            {
                if (string.IsNullOrEmpty(platformUrl) || string.IsNullOrEmpty(token))
                {
                    LogToUI("❌ No platform URL or token provided. Cannot fetch experience scores.", "error");
                    return;
                }

                LogToUI($"SENDING TO {platformUrl} with Token (last 4): {token?.Substring(token.Length - 4)}");

                if (_experienceService == null)
                    _experienceService = new ExperienceService(platformUrl, token, _logTextBox);
                else
                    _experienceService.UpdateContext(platformUrl, token);

                // Determine metrics once per query
                var selectedMetrics = await _metricsHelper.GetActiveOrDefaultMetricsAsync();
                _lastUsedExperienceMetrics = selectedMetrics;

                // 🔍 Try FQDNs first
                var fqdnList = overrideFqdns ?? GetSelectedFqdns();
                if (fqdnList != null && fqdnList.Count > 0)
                {
                    _experienceLastQueryUsedFqdns = true;
                    _lastUsedExperienceFqdns = fqdnList.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    _lastUsedExperienceFilters = null;

                    await ReloadExperienceFirstPageAsync(platformUrl, token);

                    var tabControl = this.FindControl<TabControl>("MainTabControl");
                    if (tabControl != null)
                        tabControl.SelectedIndex = 4;

                    return;
                }

                // 🧠 If no FQDNs, check for filters
                var selectedFilters = GetSelectedExperienceFilters();
                if (selectedFilters != null && selectedFilters.Any(kvp => kvp.Value?.Count > 0))
                {
                    _experienceLastQueryUsedFqdns = false;
                    _lastUsedExperienceFilters = selectedFilters;
                    _lastUsedExperienceFqdns = new List<string>();

                    await ReloadExperienceFirstPageAsync(platformUrl, token);

                    var tabControl = this.FindControl<TabControl>("MainTabControl");
                    if (tabControl != null)
                        tabControl.SelectedIndex = 4;

                    return;
                }

                LogToUI("❌ No FQDNs or filters selected to fetch experience scores.", "error");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching experience scores: {ex.Message}", "error");
            }
        }




        private List<string> GetSelectedFqdns()
        {
            return SelectedFqdnListBox?.Items?
                .Cast<string>()
                .Where(fqdn => !string.IsNullOrWhiteSpace(fqdn))
                .Distinct()
                .ToList()
                ?? new List<string>();
        }
        */
        private static string? NormalizeWildcardSearch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var s = text.Trim();

            if (s == "*" ||
                s == "*.*" ||
                s.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return s;
        }



        private async void OnFetchExperienceScoresClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string platformUrl = _selectedPlatform?.Url;
                string token = _token;

                if (string.IsNullOrEmpty(platformUrl) || string.IsNullOrEmpty(token))
                {
                    LogToUI("❌ Platform URL or token is missing. Cannot fetch experience scores.", "error");
                    return;
                }

                var fqdnBox = this.FindControl<TextBox>("ExperienceScoreFqdnTextBox");
                var raw = (fqdnBox?.Text ?? string.Empty);

                // Empty / "all" / "*" means: browse (paged) without an FQDN filter.
                var normalized = NormalizeWildcardSearch(raw);

                // ALWAYS source measures from the dropdown/listbox helper so we honor user selections.
                // This also prevents invalid "Measures" values (like "Status") from being sent.
                if (_metricsHelper != null)
                    _lastUsedExperienceMetrics = await _metricsHelper.GetActiveOrDefaultMetricsAsync();
                else
                    _lastUsedExperienceMetrics = new List<string> { "ExperienceScore", "StabilityScore", "PerformanceScore", "ResponsivenessScore" };

                if (normalized == null)
                {
                    _experienceLastQueryUsedFqdns = false;
                    _lastUsedExperienceFqdns = new List<string>();

                    // If you want "" / all / * to truly mean "browse latest", allow empty filters.
                    _experienceAllowEmptyFilterQuery = true;

                    // Clear filters so ExperienceService uses IsLatest only.
                    _lastUsedExperienceFilters = new Dictionary<string, List<string>>();

                    await ReloadExperienceFirstPageAsync(platformUrl, token);

                    var tabControl = this.FindControl<TabControl>("MainTabControl");
                    if (tabControl != null)
                        tabControl.SelectedIndex = 4;

                    return;
                }

                // Otherwise: treat as specific FQDN search
                _experienceAllowEmptyFilterQuery = false;
                //   await FetchExperienceScoresAsync(new List<string> { normalized }, platformUrl, token);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching experience scores: {ex.Message}", "error");
            }
        }




        public class ExperienceApiResponse
        {
            public string Bookmark { get; set; }
            public List<Dictionary<string, object>> Items { get; set; } = new();
            public int TotalCount { get; set; }
        }

        private List<Dictionary<string, string>> ParseExperienceResults(string resultJson)
        {
            var results = new List<Dictionary<string, string>>();

            if (string.IsNullOrWhiteSpace(resultJson))
                return results;

            try
            {
                var parsed = JObject.Parse(resultJson);
                var items = parsed["Items"] as JArray;

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var dict = new Dictionary<string, string>();

                        foreach (var prop in item.Children<JProperty>())
                        {
                            dict[prop.Name] = prop.Value?.ToString() ?? "—";
                        }

                        results.Add(dict);
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to parse experience results: {ex.Message}", "error");
            }

            return results;
        }

        private int TryGetExperiencePageSizeFromCombo()
        {
            try
            {
                var combo = this.FindControl<ComboBox>("ExperiencePageSizeCombo");
                if (combo?.SelectedItem is ComboBoxItem cbi)
                {
                    if (int.TryParse(cbi.Content?.ToString(), out var n))
                        return Math.Min(Math.Max(n, 1), ExperienceMaxPageSize);
                }

                if (combo?.SelectedItem is string s && int.TryParse(s, out var n2))
                    return Math.Min(Math.Max(n2, 1), ExperienceMaxPageSize);
            }
            catch
            {
                // ignore
            }

            return Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);
        }

        private void ExperiencePageSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _experiencePageSize = TryGetExperiencePageSizeFromCombo();

            // If we have anything currently loaded/displayed, changing page size should refresh immediately.
            // This covers:
            //  - last query used FQDNs
            //  - last query used filters
            //  - browse/latest mode (allow empty filter query)
            var hasAnyPriorQuery =
                (_experienceLastQueryUsedFqdns && _lastUsedExperienceFqdns != null && _lastUsedExperienceFqdns.Count > 0)
                || (!_experienceLastQueryUsedFqdns && _lastUsedExperienceFilters != null)
                || _experienceAllowEmptyFilterQuery
                || (_latestExperienceResults != null && _latestExperienceResults.Count > 0)
                || (_experienceResults != null && _experienceResults.Count > 0)
                || _experienceTotalCount > 0;

            if (!hasAnyPriorQuery)
                return;

            // Always reset to first page when page size changes
            _experiencePrevBookmarks.Clear();
            _experienceRequestBookmark = null;
            _experienceNextBookmark = null;
            _experiencePageIndex = 1;

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var platform = _selectedPlatform;
                    if (platform == null || string.IsNullOrWhiteSpace(platform.Url) || string.IsNullOrWhiteSpace(_token))
                        return;

                    await ReloadExperienceFirstPageAsync(platform.Url, _token);

                }
                catch
                {
                    // ignore
                }
            });
        }

        private void UpdateExperiencePagingUi()
        {
            try
            {
                var prev = this.FindControl<Button>("ExperiencePrevPageButton");
                var next = this.FindControl<Button>("ExperienceNextPageButton");
                var info = this.FindControl<TextBlock>("ExperiencePageInfoText");

                var pageSize = Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);

                // IMPORTANT: RenderExperienceResultsInResultsPanel(items) is what you use to render.
                // _filteredExperienceResults is not guaranteed to be set by that renderer,
                // so "shown" must be resilient.
                var shown = 0;
                try
                {
                    shown = _filteredExperienceResults?.Count ?? 0;
                }
                catch
                {
                    shown = 0;
                }

                // Prev is "page > 1" in Start-based paging
                if (prev != null)
                    prev.IsEnabled = _experiencePageIndex > 1;

                // Next:
                // 1) If Bookmark is present -> allow next.
                // 2) Else if TotalCount is known -> math.
                // 3) Else fallback heuristic: if we received a full page, assume there may be another page.
                bool hasNext;
                if (!string.IsNullOrWhiteSpace(_experienceNextBookmark))
                {
                    hasNext = true;
                }
                else if (_experienceTotalCount > 0)
                {
                    hasNext = (_experiencePageIndex * pageSize) < _experienceTotalCount;
                }
                else
                {
                    // Heuristic fallback:
                    // When TotalCount isn't provided, the only safe UI behavior is:
                    // if the current page returned exactly pageSize items, allow Next (it might have more).
                    // If it returned fewer, Next should be disabled.
                    hasNext = shown == pageSize;
                }

                if (next != null)
                    next.IsEnabled = hasNext;

                if (info != null)
                {
                    if (_experienceTotalCount > 0)
                    {
                        info.Text = $"Page {_experiencePageIndex} • Showing {shown} of {_experienceTotalCount}";
                    }
                    else
                    {
                        info.Text = $"Page {_experiencePageIndex} • Showing {shown}";
                    }
                }
            }
            catch
            {
                // ignore
            }
        }


        private async Task ReloadExperienceFirstPageAsync(string platformUrl, string token)
        {
            _experiencePrevBookmarks.Clear();
            _experienceRequestBookmark = null;
            _experienceNextBookmark = null;
            _experiencePageIndex = 1;
            _experienceTotalCount = 0;

            await LoadExperiencePageAsync(platformUrl, token, _experienceRequestBookmark);
        }

        private async Task LoadExperiencePageAsync(string platformUrl, string token, string? requestBookmark)
        {
            if (_experienceService == null)
                _experienceService = new ExperienceService(platformUrl, token, _logTextBox);
            else
                _experienceService.UpdateContext(platformUrl, token);

            var pageSize = Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);
            var start = ((_experiencePageIndex - 1) * pageSize) + 1;

            ExperienceService.ExperienceApiResponse page;

            if (_experienceLastQueryUsedFqdns)
            {
                var fqdns = _lastUsedExperienceFqdns ?? new List<string>();
                if (fqdns.Count == 0)
                {
                    LogToUI("❌ No FQDNs available for Experience paging.", "error");
                    return;
                }

                page = await _experienceService.FetchExperiencePageForFqdnsAsync(
                fqdnList: fqdns,
                selectedMetrics: _lastUsedExperienceMetrics,
                platformUrl: platformUrl,
                token: token,
                bookmark: requestBookmark,
                pageSize: pageSize,
                start: start);

            }
            else
            {
                var filters = _lastUsedExperienceFilters;
                if (filters == null || !filters.Any(kvp => kvp.Value?.Count > 0))
                {
                    if (!_experienceAllowEmptyFilterQuery)
                    {
                        LogToUI("❌ No filters available for Experience paging.", "error");
                        return;
                    }

                    // Explicit "all" query: proceed with IsLatest only (no additional filters)
                    filters = new Dictionary<string, List<string>>();
                }

                page = await _experienceService.FetchExperiencePageForFiltersAsync(
                selectedFilters: filters,
                selectedMetrics: _lastUsedExperienceMetrics,
                platformUrl: platformUrl,
                token: token,
                bookmark: requestBookmark,
                pageSize: pageSize,
                start: start);

            }

            _experienceRequestBookmark = requestBookmark;
            _experienceNextBookmark = page?.Bookmark;
            _experienceTotalCount = page?.TotalCount ?? 0;

            var items = page?.Items ?? new List<Dictionary<string, object>>();
            //RenderExperienceResultsInResultsPanel(items);
            LogToUI($"✅ Rendered {items.Count} experience row(s) (page {_experiencePageIndex}).");
            UpdateExperiencePagingUi();
        }

        private async void ExperienceNextPageButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var pageSize = Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);

                // If total count is known, stop at the end.
                if (_experienceTotalCount > 0 && (_experiencePageIndex * pageSize >= _experienceTotalCount))
                    return;

                _experiencePageIndex++;
                await LoadExperiencePageAsync(_selectedPlatform?.Url, _token, requestBookmark: null);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load next Experience page: {ex.Message}", "error");
            }
        }


        private async void ExperiencePrevPageButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_experiencePageIndex <= 1)
                    return;

                _experiencePageIndex = Math.Max(1, _experiencePageIndex - 1);
                await LoadExperiencePageAsync(_selectedPlatform?.Url, _token, requestBookmark: null);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load previous Experience page: {ex.Message}", "error");
            }
        }


        private async void ExperienceGoToPageButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var tb = this.FindControl<TextBox>("ExperienceGoToPageTextBox");
                if (tb == null)
                    return;

                if (!int.TryParse((tb.Text ?? string.Empty).Trim(), out var targetPage))
                {
                    LogToUI("❌ Go To Page: enter a number.", "error");
                    return;
                }

                if (targetPage < 1)
                    targetPage = 1;

                await GoToExperiencePageAsync(targetPage);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Go To Page failed: {ex.Message}", "error");
            }
        }

        private async Task<ExperienceService.ExperienceApiResponse> FetchExperiencePageOnlyAsync(string platformUrl, string token, string? requestBookmark)
        {
            if (_experienceService == null)
                _experienceService = new ExperienceService(platformUrl, token, _logTextBox);
            else
                _experienceService.UpdateContext(platformUrl, token);

            var pageSize = Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);
            var start = ((_experiencePageIndex - 1) * pageSize) + 1;

            if (_experienceLastQueryUsedFqdns)
            {
                var fqdns = _lastUsedExperienceFqdns ?? new List<string>();
                return await _experienceService.FetchExperiencePageForFqdnsAsync(
                    fqdnList: fqdns,
                    selectedMetrics: _lastUsedExperienceMetrics,
                    platformUrl: platformUrl,
                    token: token,
                    bookmark: requestBookmark,
                    pageSize: pageSize,
                    start: start);
            }

            var filters = _lastUsedExperienceFilters;
            if (filters == null || !filters.Any(kvp => kvp.Value?.Count > 0))
            {
                if (_experienceAllowEmptyFilterQuery)
                    filters = new Dictionary<string, List<string>>();
                else
                    filters = new Dictionary<string, List<string>>();
            }

            return await _experienceService.FetchExperiencePageForFiltersAsync(
                selectedFilters: filters,
                selectedMetrics: _lastUsedExperienceMetrics,
                platformUrl: platformUrl,
                token: token,
                bookmark: requestBookmark,
                pageSize: pageSize,
                start: start);
        }


        private async Task GoToExperiencePageAsync(int targetPage)
        {
            var platformUrl = _selectedPlatform?.Url;
            var token = _token;
            if (string.IsNullOrWhiteSpace(platformUrl) || string.IsNullOrWhiteSpace(token))
                return;

            // Ensure we have a query context (FQDNs or filters) before attempting to seek.
            var hasFqdns = _experienceLastQueryUsedFqdns && (_lastUsedExperienceFqdns?.Count ?? 0) > 0;
            var hasFilters = !_experienceLastQueryUsedFqdns && (_lastUsedExperienceFilters != null && _lastUsedExperienceFilters.Any(kvp => kvp.Value?.Count > 0));
            if (!hasFqdns && !hasFilters && !_experienceAllowEmptyFilterQuery)
            {
                LogToUI("❌ Run an Experience query first before using Go To Page.", "error");
                return;
            }

            if (targetPage < 1)
                targetPage = 1;

            _experiencePageIndex = targetPage;
            await LoadExperiencePageAsync(platformUrl, token, requestBookmark: null);
        }


        /*
                private void OnClearExperienceGridClicked(object sender, RoutedEventArgs e)
                {
                    var experienceResultsDataGrid = this.FindControl<DataGrid>("ExperienceResultsDataGrid");
                    experienceResultsDataGrid.ItemsSource = null;
                    experienceResultsDataGrid.Columns.Clear();
                }

                private void RenderExperienceResultsListDynamic(List<Dictionary<string, string>> experienceResults)
                {
                    if (ExperienceResultsPanel == null)
                    {
                        ExperienceResultsPanel = this.FindControl<StackPanel>("ExperienceResultsPanel");

                        if (ExperienceResultsPanel == null)
                        {
                            LogToUI("❌ ExperienceResultsPanel is still not available after trying to find it.", "error");
                            return;
                        }
                    }

                    ExperienceResultsPanel.Children.Clear();

                    if (experienceResults == null || experienceResults.Count == 0)
                    {
                        ExperienceResultsPanel.Children.Add(new TextBlock
                        {
                            Text = "⚠️ No experience results to display.",
                            Foreground = Brushes.Gray,
                            FontSize = 14,
                            FontStyle = FontStyle.Italic
                        });
                        return;
                    }

                    var limit = Math.Min(Math.Max(_experiencePageSize, 1), ExperienceMaxPageSize);
                    var limited = experienceResults.Take(limit).ToList();
                    if (experienceResults.Count > limit)
                    {
                        LogToUI($"⚠️ Showing first {limit} of {experienceResults.Count} experience rows (Page Size).", "warning");
                    }

                    foreach (var row in limited)
                    {
                        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

                        foreach (var kvp in row)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = $"{kvp.Key}: {kvp.Value}",
                                Width = 150
                            });
                        }

                        ExperienceResultsPanel.Children.Add(panel);
                    }
                }


                private async void FetchExperienceButton_Click(object sender, RoutedEventArgs e)
                {
                    try
                    {
                        string platformUrl = _selectedPlatform?.Url;
                        string token = _token;

                        if (string.IsNullOrEmpty(platformUrl) || string.IsNullOrEmpty(token))
                        {
                            LogToUI("❌ Platform URL or token is missing. Cannot fetch experience scores.", "error");
                            return;
                        }

                        var fqdnBox = this.FindControl<TextBox>("ExperienceScoreFqdnTextBox");
                        var raw = (fqdnBox?.Text ?? string.Empty);

                        // "" / "all" / "*" => browse using selected filters (or none)
                        var normalized = NormalizeWildcardSearch(raw);

                        // Always honor measures selected in the Measures UI
                        if (_metricsHelper != null)
                            _lastUsedExperienceMetrics = await _metricsHelper.GetActiveOrDefaultMetricsAsync();
                        else
                            _lastUsedExperienceMetrics = new List<string> { "ExperienceScore", "StabilityScore", "PerformanceScore", "ResponsivenessScore" };

                        if (normalized == null)
                        {
                            // Use listbox filters (MgmtGroup/Location/Model/OS/Criticality)
                            var selectedFilters = GetSelectedExperienceFilters() ?? new Dictionary<string, List<string>>();

                            // Keep only filters with selections
                            selectedFilters = selectedFilters
                                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                            _experienceLastQueryUsedFqdns = false;
                            _lastUsedExperienceFqdns = new List<string>();
                            _lastUsedExperienceFilters = selectedFilters;

                            // Allow empty filter query (IsLatest=true only) when ""/all/*
                            _experienceAllowEmptyFilterQuery = true;

                            await ReloadExperienceFirstPageAsync(platformUrl, token);

                            var tabControl = this.FindControl<TabControl>("MainTabControl");
                            if (tabControl != null)
                                tabControl.SelectedIndex = 4;

                            return;
                        }

                        // Otherwise treat it as a specific FQDN lookup
                        _experienceAllowEmptyFilterQuery = false;
                        await FetchExperienceScoresAsync(new List<string> { normalized }, platformUrl, token);
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Error fetching experience scores: {ex.Message}", "error");
                    }
                }



                private void ToggleSortOrderExperience(string column, List<Dictionary<string, object>> results)
                {
                    // If the column is not already in the dictionary, set its sort order to Ascending by default
                    if (!_columnSortOrders.ContainsKey(column))
                    {
                        _columnSortOrders[column] = SortOrder.Ascending;
                    }

                    // Toggle the sort order: Ascending -> Descending -> None -> Ascending
                    var currentSortOrder = _columnSortOrders[column];
                    if (currentSortOrder == SortOrder.Ascending)
                    {
                        _columnSortOrders[column] = SortOrder.Descending;
                    }
                    else if (currentSortOrder == SortOrder.Descending)
                    {
                        _columnSortOrders[column] = SortOrder.None;
                    }
                    else
                    {
                        _columnSortOrders[column] = SortOrder.Ascending;
                    }

                    // Apply sorting based on the updated sort order
                    SortDataExperience(column, _columnSortOrders[column], results);
                }

                // Sorting function to apply sorting based on the column and current sort order
                private void SortDataExperience(string column, SortOrder sortOrder, List<Dictionary<string, object>> results)
                {
                    // Perform sorting based on the sort order
                    var sortedData = sortOrder switch
                    {
                        SortOrder.Ascending => results.OrderBy(row => row[column]?.ToString()).ToList(),
                        SortOrder.Descending => results.OrderByDescending(row => row[column]?.ToString()).ToList(),
                        _ => results // No sorting
                    };

                    // Re-render the sorted results
                    RenderExperienceResultsInResultsPanel(sortedData);
                }
                private void RenderExperienceResultsInResultsPanel(List<Dictionary<string, object>> data)
                {
                    _filteredExperienceResults = data
                        .Select(row => row
                            .Where(kvp => kvp.Key != "TachyonGuid" && kvp.Key != "Timestamp")
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
                        .ToList();

                    _experienceFqdnCheckboxes = new();
                    _experienceColumnFilters = new();
                    _experienceColumns = _filteredExperienceResults.SelectMany(d => d.Keys).Distinct().ToList();

                    var resultsPanel = this.FindControl<StackPanel>("ExperienceResultsPanel");
                    resultsPanel.Children.Clear();

                    if (_filteredExperienceResults.Count == 0)
                    {
                        LogToUI("⚠️ No experience results to display.");
                        return;
                    }

                    var listArea = new StackPanel();



                    // Compute stable widths so Experience header + rows stay aligned even with many measures selected.


                    // Use the measure Title (when available) as the display name for width computation so headers don't truncate unnecessarily.


                    var experienceDisplayNames = new List<string>(_experienceColumns.Count);


                    foreach (var col in _experienceColumns)


                    {


                        var measureForName = _experienceMeasures.FirstOrDefault(m => m.Name == col);


                        experienceDisplayNames.Add(measureForName?.Title ?? col);


                    }



                    var experienceRowsForWidth = _filteredExperienceResults


                        .Select(r => (IReadOnlyDictionary<string, string>)r.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? ""))


                        .ToList();



                    ComputeAndStoreGridColumnWidths(


                        _experienceColumns,


                        experienceDisplayNames,


                        experienceRowsForWidth,


                        GridWidthContext.Experience);



                    // GenerateGridColumns already includes the leading checkbox/spacer column.


                    var sharedColumns = GenerateGridColumns(_experienceColumns, GridWidthContext.Experience);
                    var headerGrid = new Grid
                    {
                        ColumnDefinitions = sharedColumns,
                        Margin = new Thickness(0)
                    };

                    var spacer = new Border { Width = 30 };
                    Grid.SetColumn(spacer, 0);
                    headerGrid.Children.Add(spacer);

                    var filterBoxes = new Dictionary<string, TextBox>();

                    for (int i = 0; i < _experienceColumns.Count; i++)
                    {
                        var colName = _experienceColumns[i];
                        _experienceGridColumnWidths.TryGetValue(colName, out var colWidth);
                        if (colWidth <= 0) colWidth = GridDataColumnMinWidth;

                        var innerGrid = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions
                            {
                                new ColumnDefinition(GridLength.Star),
                                new ColumnDefinition(GridLength.Auto)
                            },
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Margin = new Thickness(GridColumnGap / 2, 0, GridColumnGap / 2, 0)
                        };

                        var measure = _experienceMeasures.FirstOrDefault(m => m.Name == colName);
                        string displayName = measure?.Title ?? colName;
                        string tooltip = measure != null
                            ? $"{measure.Title}\n\n{measure.Notes ?? measure.Description ?? ""}".Trim()
                            : colName;

                        var filterBox = new TextBox
                        {
                            Watermark = $"Filter {displayName}",
                            MinWidth = GridDataColumnMinWidth,
                            MaxWidth = GridDataColumnMaxWidth,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            TextWrapping = TextWrapping.NoWrap,
                            Margin = new Thickness(0),
                            Padding = new Thickness(4, 2, 4, 2)
                        };

                        // Restore prior filter text for this column.
                        if (_experienceFilterText.TryGetValue(colName, out var savedFilter) && !string.IsNullOrEmpty(savedFilter))
                        {
                            filterBox.Text = savedFilter;
                        }

                        // Keep filter state updated as user types.
                        filterBox.GetObservable(TextBox.TextProperty)
                                 .Throttle(TimeSpan.FromMilliseconds(150))
                                 .Subscribe(t =>
                                 {
                                     var txt = t?.Trim() ?? string.Empty;
                                     if (string.IsNullOrEmpty(txt))
                                         _experienceFilterText.Remove(colName);
                                     else
                                         _experienceFilterText[colName] = txt;
                                 });
                        ToolTip.SetTip(filterBox, tooltip);
                        filterBoxes[colName] = filterBox;
                        Grid.SetColumn(filterBox, 0);

                        var sortButton = new Button
                        {
                            Content = "⇅",
                            Width = 24,
                            Height = 24,
                            Padding = new Thickness(0),
                            Margin = new Thickness(2, 0, 0, 0),
                            Tag = colName
                        };
                        sortButton.Click += (s, e) =>
                        {
                            _experienceSortAsc = !_experienceSortAsc;
                            SortExperienceResults(colName, _experienceSortAsc);
                            ApplyExperienceFilters(_experienceColumnFilters, _experienceColumns, listArea);
                        };
                        Grid.SetColumn(sortButton, 1);

                        innerGrid.Children.Add(filterBox);
                        innerGrid.Children.Add(sortButton);
                        Grid.SetColumn(innerGrid, i + 1);
                        headerGrid.Children.Add(innerGrid);
                    }

                    _experienceColumnFilters = filterBoxes;

                    var verticalScrollViewer = new ScrollViewer
                    {
                        Content = listArea,
                        Height = 450,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };

                    var toolbar = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Spacing = 10,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    var selectAllCheckbox = new CheckBox
                    {
                        Content = "Select All FQDNs",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    selectAllCheckbox.Checked += (_, __) =>
                    {
                        foreach (var cb in _experienceFqdnCheckboxes.Values) cb.IsChecked = true;
                    };
                    selectAllCheckbox.Unchecked += (_, __) =>
                    {
                        foreach (var cb in _experienceFqdnCheckboxes.Values) cb.IsChecked = false;
                    };

                    var sendSelectedToFqdnListButton = new Button
                    {
                        Content = "Send Selected → FQDN List",
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    sendSelectedToFqdnListButton.Click += (_, __) =>
                    {
                        SendSelectedExperienceFqdnsToFqdnList();
                    };

                    var clearFqdnListButton = new Button
                    {
                        Content = "Clear FQDN List",
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    clearFqdnListButton.Click += (_, __) =>
                    {
                        OnClearFqdnsClicked(null, new RoutedEventArgs());
                    };

                    var fqdnListCountLabel = new TextBlock
                    {
                        Text = "0",
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    _experienceFqdnListCountText = fqdnListCountLabel;
                    UpdateFqdnListCountLabels();

                    var clearFiltersButton = new Button
            {
                Content = "Clear Filters",
                Padding = new Thickness(8, 2, 8, 2)
            };
            clearFiltersButton.Click += (_, __) =>
            {
                if (filterBoxes == null || filterBoxes.Count == 0)
                    return;

                _suppressResultsAutoFilterApply = true;
                try
                {
                    foreach (var box in filterBoxes.Values)
                        box.Text = string.Empty;

                    _resultsFilterText.Clear();
                }
                finally
                {
                    _suppressResultsAutoFilterApply = false;
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    if (_resultsServerFilterAllCheckBox?.IsChecked == true)
                    {
                        await TriggerResultsServerRefilterAsync();
                        return;
                    }

                    ApplyFilters(filterBoxes, columns, listArea);
                });
            };


                    var exportShownButton = new Button
                    {
                        Content = "Export Shown",
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    exportShownButton.Click += async (_, __) =>
                    {
                        if (_filteredExperienceResults == null || !_filteredExperienceResults.Any())
                        {
                            LogToUI("⚠️ No experience results to export.");
                            return;
                        }

                        string format = GetConfiguredExportFormatOrDefault("csv");

                        var dialog = new SaveFileDialog
                        {
                            Title = "Export Filtered Results",
                            InitialFileName = $"experience_filtered.{format}",
                            DefaultExtension = format
                        };

                        var filePath = await dialog.ShowAsync(this);
                        if (string.IsNullOrWhiteSpace(filePath)) return;
                        if (!filePath.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase))
                            filePath += $".{format}";

                        var stringified = _filteredExperienceResults
                            .Select(d => d.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? ""))
                            .ToList();

                        await ExportHelper.ExportDictionaryListAsync(stringified, filePath, format, _logTextBox);
                    };

                    var exportAllButton = new Button
                    {
                        Content = "Export All",
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    exportAllButton.Click += async (_, __) =>
                    {
                        if (_experienceMeasures == null || _selectedPlatform == null || string.IsNullOrEmpty(_token))
                        {
                            LogToUI("❌ Platform, token, or metrics not available.");
                            return;
                        }

                        string format = GetConfiguredExportFormatOrDefault("csv");

                        var dialog = new SaveFileDialog
                        {
                            Title = "Export All Results",
                            InitialFileName = $"experience_full.{format}",
                            DefaultExtension = format
                        };

                        var filePath = await dialog.ShowAsync(this);
                        if (string.IsNullOrWhiteSpace(filePath)) return;
                        if (!filePath.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase))
                            filePath += $".{format}";

                        var filters = _experienceColumnFilters
                            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.Text))
                            .Select(kvp => new ExperienceFilter
                            {
                                Attribute = kvp.Key,
                                Operator = "contains",
                                Value = kvp.Value.Text
                            })
                            .ToList();
                        var filtersDict = _experienceColumnFilters
                            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.Text))
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => new List<string> { kvp.Value.Text });

                        _activeExperienceFiltersDict = filtersDict;
                        // ✅ Log actual filter and metric values
                        var selectedMetrics = await _metricsHelper.GetActiveOrDefaultMetricsAsync();
                        var selectedFilters = GetSelectedExperienceFilters();
                        LogToUI($"📎 Filters passed to Export:\n{JsonConvert.SerializeObject(selectedFilters, Formatting.Indented)}\n");
                        LogToUI($"📎 Metrics passed to Export:\n{JsonConvert.SerializeObject(selectedMetrics, Formatting.Indented)}\n");

                        var exportPanel = this.FindControl<StackPanel>("ExportStatusPanel");
                        var exportBar = this.FindControl<ProgressBar>("ExportProgressBar");
                        var exportLabel = this.FindControl<TextBlock>("ExportProgressLabel");

                        if (exportPanel != null) exportPanel.IsVisible = true;
                        if (exportBar != null) exportBar.Value = 0;
                        if (exportLabel != null) exportLabel.Text = "Exporting...";

                        var progress = new Progress<(int RowCount, int TotalCount, double ElapsedSeconds)>(state =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                int rowCount = state.RowCount;
                                int totalCount = state.TotalCount;
                                double elapsedSeconds = state.ElapsedSeconds;

                                double percent = totalCount > 0 ? (rowCount * 100.0 / totalCount) : 0;

                                if (percent > 0)
                                {
                                    double estimatedTotalSeconds = elapsedSeconds / (percent / 100.0);
                                    double remainingSeconds = estimatedTotalSeconds - elapsedSeconds;
                                    TimeSpan eta = TimeSpan.FromSeconds(remainingSeconds);

                                    ExportProgressLabel.Text =
                                        $"Exported {rowCount:N0} of {totalCount:N0} rows ({percent:N1}%) – ETA: {eta:mm\\:ss}";
                                }
                                else
                                {
                                    ExportProgressLabel.Text = $"Exported {rowCount:N0} of {totalCount:N0} rows...";
                                }

                                ExportProgressBar.Value = percent;
                            });
                        });




                        // ✅ Call ExportHelper with actual UI filter state
                        await ExportHelper.ExportExperienceResultsAsync(
                            _selectedPlatform.Url,
                            _token,
                            selectedMetrics,
                            selectedFilters,
                            filePath,
                            format,
                            _logTextBox,
                            progress);

                        if (exportPanel != null) exportPanel.IsVisible = false;

                    };

                    toolbar.Children.Add(selectAllCheckbox);
                    toolbar.Children.Add(sendSelectedToFqdnListButton);
                    toolbar.Children.Add(clearFqdnListButton);
                    toolbar.Children.Add(new TextBlock { Text = "FQDNs:", VerticalAlignment = VerticalAlignment.Center });
                    toolbar.Children.Add(fqdnListCountLabel);
                    toolbar.Children.Add(clearFiltersButton);
                    toolbar.Children.Add(exportShownButton);
                    toolbar.Children.Add(exportAllButton);

                    var container = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 6
                    };

                    container.Children.Add(toolbar);
                    var horizontalStack = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 6
                    };
                    horizontalStack.Children.Add(new Border
                    {
                        Padding = new Thickness(4, 0, 4, 0),
                        Child = headerGrid
                    });
                    horizontalStack.Children.Add(verticalScrollViewer);

                    var horizontalScrollViewer = new ScrollViewer
                    {
                        Content = horizontalStack,
                        Height = 520, // header + rows viewport
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
                    };

                    container.Children.Add(horizontalScrollViewer);
                    resultsPanel.Children.Add(container);

                    foreach (var box in _experienceColumnFilters.Values)
                    {
                        box.GetObservable(TextBox.TextProperty)
                           .Throttle(TimeSpan.FromMilliseconds(300))
                           .Skip(1)
                           .Subscribe(_ =>
                           {
                               if (_suppressExperienceAutoFilterApply)
                                   return;

                               Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                               {
                                   if (_suppressExperienceAutoFilterApply)
                                       return;

                                   ApplyExperienceFilters(_experienceColumnFilters, _experienceColumns, listArea);
                               });
                           });
                    }


                    ApplyExperienceFilters(_experienceColumnFilters, _experienceColumns, listArea);
                }
        */

        private void FilterSearchBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            var query = textBox.Text?.Trim().ToLowerInvariant();

            foreach (var kvp in _experienceFilterInputs)
            {
                var control = kvp.Value;
                if (string.IsNullOrWhiteSpace(query))
                {
                    control.IsVisible = true;
                }
                else
                {
                    control.IsVisible = kvp.Key.ToLowerInvariant().Contains(query);
                }
            }
        }

        private void FilterListBoxItems(string listBoxName, string filter)
        {
            var listBox = this.FindControl<ListBox>(listBoxName);
            if (listBox == null || listBox.Items == null) return;

            var allItems = listBox.Items.Cast<string>().ToList();
            listBox.ItemsSource = string.IsNullOrEmpty(filter)
                ? allItems
                : allItems.Where(i => i?.ToLower().Contains(filter) == true).ToList();
        }

        private async Task<string> PromptForExportPath(string format)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Experience Results",
                InitialFileName = $"experience_results.{format}",
                DefaultExtension = format
            };
            var filePath = await dialog.ShowAsync(this);
            if (!filePath?.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase) ?? true)
                filePath += $".{format}";
            return filePath;
        }

        private async void ApplyExperienceFilters(Dictionary<string, TextBox> filters, List<string> columns, StackPanel container)

        {
            container.Children.Clear();

            var filtered = _filteredExperienceResults.Where(row =>
            {
                foreach (var col in columns)
                {
                    if (filters.TryGetValue(col, out var filterBox))
                    {
                        var filterText = filterBox.Text?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(filterText))
                        {
                            if (!row.TryGetValue(col, out var value) || value == null)
                                return false;

                            if (!MatchesFilter(value.ToString() ?? string.Empty, filterText))
                                return false;
                        }
                    }
                }
                return true;
            }).ToList();

            _experienceFqdnCheckboxes.Clear();
            var fqdnSet = new HashSet<string>();

            foreach (var row in filtered)
            {
                var rowGrid = new Grid
                {
                    // GenerateGridColumns already includes the leading checkbox/spacer column.
                    ColumnDefinitions = GenerateGridColumns(columns, GridWidthContext.Experience),
                    Margin = new Thickness(0),
                    Background = Brushes.Transparent
                };

                CheckBox checkbox = null;
                if (row.TryGetValue("Fqdn", out var fqdnObj) && fqdnObj != null)
                {
                    var fqdn = fqdnObj.ToString();
                    if (!fqdnSet.Contains(fqdn))
                    {
                        checkbox = new CheckBox
                        {
                            IsChecked = _selectedResultFqdns.Contains(fqdn),
                            Tag = fqdn,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            // Column 0 is a fixed 30px checkbox/spacer column.
                            // Any horizontal margin here will spill into column 1 and visually misalign rows.
                            Margin = new Thickness(0)
                        };
                        _experienceFqdnCheckboxes[fqdn] = checkbox;
                        fqdnSet.Add(fqdn);
                    }
                }

                if (checkbox != null)
                {
                    Grid.SetColumn(checkbox, 0);
                    rowGrid.Children.Add(checkbox);
                }
                else
                {
                    var spacer = new Border { Width = 30 };
                    Grid.SetColumn(spacer, 0);
                    rowGrid.Children.Add(spacer);
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    var value = row.TryGetValue(col, out var val) ? val?.ToString() ?? "" : "";


                    var cellText = new TextBlock
                    {
                        Text = value,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = TextAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    // Double-click any cell to auto-fill the corresponding filter.
                    // (Attach to the cell border so the entire cell area is hit-testable.)

                    // 🎯 Tooltip
                    var measure = _experienceMeasures.FirstOrDefault(m => m.Name == col);
                    var title = measure?.Title ?? col;
                    var notes = measure?.Notes ?? measure?.Description ?? "";

                    var tooltipText = title;

                    if (!string.IsNullOrWhiteSpace(value))
                        tooltipText += System.Environment.NewLine + System.Environment.NewLine + value;

                    if (!string.IsNullOrWhiteSpace(notes))
                        tooltipText += System.Environment.NewLine + System.Environment.NewLine + notes;

                    var cellBorder = new Border
                    {
                        Padding = new Thickness(4, 2, 4, 2),
                        Margin = new Thickness(GridColumnGap / 2, 0, GridColumnGap / 2, 0),
                        ClipToBounds = true,
                        Child = cellText
                    };

                    if (cellBorder.Background == null)
                        cellBorder.Background = Brushes.Transparent;

                    cellBorder.DoubleTapped += (_, __) =>
                    {
                        if (filters.TryGetValue(col, out var fb))
                        {
                            fb.Text = value;
                            ApplyExperienceFilters(filters, columns, container);
                        }
                    };

                    ToolTip.SetTip(cellBorder, tooltipText);
                    ToolTip.SetPlacement(cellBorder, PlacementMode.Pointer);

                    // 🎨 Score coloring (apply on the border so the background fills the full cell width)
                    if (double.TryParse(value, out var score) &&
                        (col.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
                         col.Contains("Performance", StringComparison.OrdinalIgnoreCase) ||
                         col.Equals("Responsiveness", StringComparison.OrdinalIgnoreCase)))
                    {
                        cellText.TextAlignment = TextAlignment.Center;

                        var theme = ActualThemeVariant;
                        bool isDark = theme == ThemeVariant.Dark;

                        if (score >= 90)
                        {
                            cellBorder.Background = new SolidColorBrush(Color.Parse(isDark ? "#2E7D32" : "#81C784"));
                            cellText.Foreground = Brushes.White;
                        }
                        else if (score >= 75)
                        {
                            cellBorder.Background = new SolidColorBrush(Color.Parse(isDark ? "#FBC02D" : "#FFF176"));
                            cellText.Foreground = Brushes.Black;
                        }
                        else
                        {
                            cellBorder.Background = new SolidColorBrush(Color.Parse(isDark ? "#C62828" : "#EF9A9A"));
                            cellText.Foreground = Brushes.White;
                        }
                    }

                    Grid.SetColumn(cellBorder, i + 1); // ⚠️ offset by 1
                    rowGrid.Children.Add(cellBorder);
                }

                var rowBorder = new Border
                {
                    Padding = new Thickness(4, 0, 4, 0),
                    Child = rowGrid
                };

                container.Children.Add(rowBorder);
            }
        }

        /*

                private List<string> _experienceColumns = new();

                private bool _experienceSortAsc = true;

                private void OnExperienceSortClicked(string columnName)
                {
                    _experienceSortAsc = !_experienceSortAsc;
                    SortExperienceResults(columnName, _experienceSortAsc);
                    ApplyExperienceFilters(_experienceColumnFilters, _experienceColumns, ExperienceResultsPanel);
                }




                private Dictionary<string, bool> _columnSortOrder = new Dictionary<string, bool>();




                private Dictionary<string, SortOrder> _currentExperienceSortOrder = new Dictionary<string, SortOrder>();
                private Dictionary<string, SortOrder> _columnSortOrders = new Dictionary<string, SortOrder>();
        */

        public enum SortOrder
        {
            Ascending,
            Descending,
            None
        }




        private List<Dictionary<string, string>> ParseExperienceJsonToStringMaps(string resultJson)
        {
            var parsedRoot = JsonConvert.DeserializeObject<ExperienceApiResponse>(resultJson);
            if (parsedRoot?.Items == null || parsedRoot.Items.Count == 0)
                return new List<Dictionary<string, string>>();

            return parsedRoot.Items
                .Select(item => item.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.ToString() ?? "—"))
                .ToList();
        }

        private double MeasureTextWidth(string text, double fontSize = 13, string fontFamily = "Segoe UI")
        {
            if (string.IsNullOrEmpty(text)) return 0;

            try
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontFamily = new FontFamily(fontFamily),
                    TextWrapping = TextWrapping.NoWrap
                };

                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return Math.Ceiling(tb.DesiredSize.Width + 20); // Add padding
            }
            catch
            {
                // Fallback heuristic
                return Math.Min(1200, Math.Ceiling(text.Length * (fontSize * 0.6) + 20));
            }
        }
        /*

                private async void OnSendSelectedFqdnsToList_Click(object? sender, RoutedEventArgs e)
                {
                    try
                    {
                        if (_resultsServerFilterAllCheckBox?.IsChecked == true)
                        {
                            await SendAllFilteredResultsFqdnsToFqdnListAsync();
                            return;
                        }
                    }
                    catch
                    {
                        // fall back to normal selection-based send
                    }

                    SendSelectedFqdnsToFqdnList();
                }

                private bool GetSwitchToFqdnTabAfterSend()
                {
                    try
                    {
                        if (_configHelper == null)
                            return true;

                        var cfg = _configHelper.GetConfiguration();
                        if (cfg == null)
                            return true;

                        if (bool.TryParse(cfg["SwitchToFqdnTabAfterSend"], out var val))
                            return val;

                        return true;
                    }
                    catch
                    {
                        return true;
                    }
                }

                private void SendSelectedFqdnsToFqdnList()
                {
                    var selectedFqdns = _fqdnCheckboxes
                        .Where(kvp => kvp.Value.IsChecked == true)
                        .Select(kvp =>
                        {
                            var rawKey = kvp.Key;
                            var fqdnPart = rawKey.Contains("||") ? rawKey.Split("||")[0] : rawKey;
                            return fqdnPart.Trim();
                        })
                        .Where(fqdn => !string.IsNullOrWhiteSpace(fqdn))
                        .Distinct()
                        .OrderBy(fqdn => fqdn)
                        .ToList();

                    if (selectedFqdns.Count == 0)
                    {
                        LogToUI("⚠️ No FQDNs selected to send to FQDN List.");
                        return;
                    }

                    var existing = (SelectedFqdnListBox.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();

                    var merged = existing
                        .Concat(selectedFqdns)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    SelectedFqdnListBox.ItemsSource = merged;
                    UpdateFqdnListCountLabels();

                    if (GetSwitchToFqdnTabAfterSend())
                        MainTabControl.SelectedIndex = 1;

                    var existingCount = existing
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    var addedCount = Math.Max(0, merged.Count - existingCount);
                    LogToUI($"✅ Added {addedCount} FQDN(s) to FQDN List (Total: {merged.Count}).");
                }

                private async Task SendAllFilteredResultsFqdnsToFqdnListAsync()
                {
                    try
                    {
                        if (_lastInstructionId <= 0)
                        {
                            LogToUI("⚠️ No instruction results are loaded.");
                            return;
                        }

                        if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
                        {
                            LogToUI("❌ No platform selected.");
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
                        {
                            LogToUI("❌ Not authenticated.");
                            return;
                        }

                        // Capture active filter expressions from the current Results filter header textboxes.
                        var activeFilters = new List<(string Column, string FilterText)>();
                        try
                        {
                            if (_columnFilters != null)
                            {
                                foreach (var kvp in _columnFilters)
                                {
                                    var col = kvp.Key;
                                    var txt = kvp.Value?.Text?.Trim() ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(col) && !string.IsNullOrWhiteSpace(txt))
                                        activeFilters.Add((col, txt));
                                }
                            }
                        }
                        catch { }

                        // Fetch all pages and apply the same client-side filter logic across the full dataset.
                        var instructionId = _lastInstructionId;
                        var pageSize = Math.Max(1, GetResultsPageSize());
                        var endpoint = $"consumer/Responses/{instructionId}";
                        var url = $"https://{_selectedPlatform.Url}/{endpoint}";

                        var allFqdns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        string? startRange = "0;0";
                        string? nextRange = null;
                        int pages = 0;

                        // Safety limit: if we know expected total rows, cap pages accordingly; otherwise cap to a generous max.
                        var maxPages = 2000;
                        if (_resultsTotalRowsExpected > 0)
                        {
                            maxPages = (int)Math.Ceiling(_resultsTotalRowsExpected / (double)pageSize) + 2;
                            if (maxPages < 1) maxPages = 1;
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                        client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                        while (!string.IsNullOrWhiteSpace(startRange) && pages < maxPages)
                        {
                            pages++;

                            var postPayload = new
                            {
                                Filter = (object?)null,
                                Start = startRange,
                                PageSize = pageSize
                            };

                            var content = new StringContent(JsonConvert.SerializeObject(postPayload), Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(url, content);
                            var json = await response.Content.ReadAsStringAsync();

                            if (!response.IsSuccessStatusCode)
                            {
                                LogToUI($"❌ Error fetching full results for filter-all: {response.StatusCode} - {json}");
                                break;
                            }

                            var parsed = JObject.Parse(json);
                            var array = parsed["Responses"] as JArray;
                            nextRange = parsed["Range"]?.ToString();

                            if (array == null || array.Count == 0)
                                break;

                            foreach (var responseItem in array)
                            {
                                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                                var fqdn = responseItem["Fqdn"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(fqdn))
                                    row["Fqdn"] = fqdn;

                                var values = responseItem["Values"] as JObject;
                                if (values != null)
                                {
                                    foreach (var prop in values.Properties())
                                        row[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                                }

                                if (row.Count == 0)
                                    continue;

                                bool match = true;
                                foreach (var f in activeFilters)
                                {
                                    if (!row.TryGetValue(f.Column, out var cell) || cell == null)
                                    {
                                        match = false;
                                        break;
                                    }

                                    if (!MatchesFilter(cell, f.FilterText))
                                    {
                                        match = false;
                                        break;
                                    }
                                }

                                if (!match)
                                    continue;

                                if (row.TryGetValue("Fqdn", out var fqdnVal) && !string.IsNullOrWhiteSpace(fqdnVal))
                                    allFqdns.Add(fqdnVal.Trim());
                            }

                            if (string.IsNullOrWhiteSpace(nextRange) || string.Equals(nextRange.Trim(), startRange.Trim(), StringComparison.OrdinalIgnoreCase))
                                break;

                            startRange = nextRange;
                        }

                        if (allFqdns.Count == 0)
                        {
                            LogToUI("⚠️ No FQDNs matched the current Results filters.");
                            return;
                        }

                        var existing = (SelectedFqdnListBox.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
                        var merged = existing
                            .Concat(allFqdns)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        SelectedFqdnListBox.ItemsSource = merged;
                        UpdateFqdnListCountLabels();

                        if (GetSwitchToFqdnTabAfterSend())
                            MainTabControl.SelectedIndex = 1;

                        var existingCount = existing
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                        var addedCount = Math.Max(0, merged.Count - existingCount);
                        LogToUI($"✅ Added {addedCount} FQDN(s) to FQDN List from filtered results (Total: {merged.Count}).");
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Failed to send filtered results to FQDN List: {ex.Message}");
                    }
                }


                private void SendSelectedExperienceFqdnsToFqdnList()
                {
                    try
                    {
                        var selectedFqdns = _experienceFqdnCheckboxes
                            .Where(kvp => kvp.Value?.IsChecked == true)
                            .Select(kvp => (kvp.Key ?? string.Empty).Trim())
                            .Where(fqdn => !string.IsNullOrWhiteSpace(fqdn))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(fqdn => fqdn, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (selectedFqdns.Count == 0)
                        {
                            LogToUI("⚠️ No Experience FQDNs selected to send to FQDN List.");
                            return;
                        }

                        var existing = (SelectedFqdnListBox.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();

                        var merged = existing
                            .Concat(selectedFqdns)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        SelectedFqdnListBox.ItemsSource = merged;
                        UpdateFqdnListCountLabels();

                        if (GetSwitchToFqdnTabAfterSend())
                            MainTabControl.SelectedIndex = 1;

                        var existingCount = existing
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                        var addedCount = Math.Max(0, merged.Count - existingCount);
                        LogToUI($"✅ Added {addedCount} FQDN(s) from Experience to FQDN List (Total: {merged.Count}).");
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Failed sending Experience selection to FQDN List: {ex.Message}");
                    }
                }


                private void UpdateFqdnListCountLabels()
                {
                    try
                    {
                        var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
                        var countLabel = this.FindControl<TextBlock>("FqdnCountLabel");

                        var count = 0;
                        if (listBox?.ItemsSource is IEnumerable<string> src)
                            count = src.Count();
                        else if (listBox?.Items != null)
                            count = listBox.Items.Cast<object>().OfType<string>().Count();

                        if (countLabel != null)
                            countLabel.Text = $"Selected FQDNs ({count})";

                        if (_resultsFqdnListCountText != null)
                            _resultsFqdnListCountText.Text = count.ToString();

                        if (_experienceFqdnListCountText != null)
                            _experienceFqdnListCountText.Text = count.ToString();
                    }
                    catch { }
                }




                private void ClearExperienceFiltersButton_Click(object sender, RoutedEventArgs e)
                {
                    ExperienceMgmtGroupListBox?.SelectedItems?.Clear();
                    ExperienceLocationListBox?.SelectedItems?.Clear();
                    ExperienceModelListBox?.SelectedItems?.Clear();
                    ExperienceOsListBox?.SelectedItems?.Clear();
                    ExperienceCriticalityListBox?.SelectedItems?.Clear();
                    MeasureCheckboxList.ItemsSource = new List<SelectableMeasure>();

                }

                */

        private void Log(string message)
        {
            LogTextBox.Text += $"{DateTime.Now:T} {message}\n";
        }

  

        private async Task UpdatePreviewAsync()
        {
            LogToUI("🧪 ENTERED UpdatePreviewAsync\n");
            var loadingLabel = this.FindControl<TextBlock>("PreviewLoadingLabel");
            var estimatedLabel = this.FindControl<TextBlock>("EstimatedCountLabel");
            var onlineLabel = this.FindControl<TextBlock>("OnlineCountLabel");
            var offlineLabel = this.FindControl<TextBlock>("OfflineCountLabel");

            if (loadingLabel != null)
                loadingLabel.IsVisible = true;

            try
            {
                if (_selectedInstruction == null || _selectedPlatform == null || string.IsNullOrWhiteSpace(_token))
                {
                    if (estimatedLabel != null) estimatedLabel.Text = "Estimated: ?";
                    if (onlineLabel != null) onlineLabel.Text = "Online: ?";
                    if (offlineLabel != null) offlineLabel.Text = "Offline: ?";
                    return;
                }

                var targetingMode = GetSelectedTargetingMode();
                LogToUI($"🔍 Targeting Mode: {targetingMode}\n");

                JObject scopeQuery;

                if (!string.Equals(targetingMode, "FQDN", StringComparison.OrdinalIgnoreCase))
                    HideInstructionFqdnPicker();

                if (string.Equals(targetingMode, "Dynamic", StringComparison.OrdinalIgnoreCase))
                {
                    scopeQuery = JObject.FromObject(BuildDynamicScopeExpression());
                    LogToUI($"🔍 DYNAMIC Scope Mode: {scopeQuery}\n");
                }
                else if (string.Equals(targetingMode, "FQDN", StringComparison.OrdinalIgnoreCase))
                {
                    void SetPreviewUnknown()
                    {
                        if (estimatedLabel != null) estimatedLabel.Text = "Estimated: ?";
                        if (onlineLabel != null) onlineLabel.Text = "Online: ?";
                        if (offlineLabel != null) offlineLabel.Text = "Offline: ?";
                        _hasPreviewedTargets = false;
                        UpdateRunButtonState();
                    }
                    void SetPreviewFromExplicitSelection(IReadOnlyCollection<string> explicitFqdns)
                    {
                        var count = explicitFqdns?.Count ?? 0;

                        if (estimatedLabel != null)
                            estimatedLabel.Text = $"Estimated: {count}";

                        if (onlineLabel != null)
                            onlineLabel.Text = "Online: ?";

                        if (offlineLabel != null)
                            offlineLabel.Text = "Offline: ?";

                        _hasPreviewedTargets = count > 0;
                        UpdateRunButtonState();
                    }

                    static bool IsBrowseLikeValue(string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                            return true;

                        var s = value.Trim();

                        return s.Length == 0 ||
                               string.Equals(s, "*", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(s, "*.*", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(s, "all", StringComparison.OrdinalIgnoreCase) ||
                               s == "%" ||
                               s.Contains('%');
                    }

                    List<string> fqdns;
                    bool usingManagerSelection = _fqdnManagerSelectedItems != null && _fqdnManagerSelectedItems.Count > 0;

                    if (usingManagerSelection)
                    {
                        fqdns = _fqdnManagerSelectedItems
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(10)
                            .ToList();
                    }
                    else
                    {
                        var raw = _fqdnTextBox?.Text ?? string.Empty;

                        // Treat browse-like textbox values as "no exact preview requested".
                        if (IsBrowseLikeValue(raw))
                        {
                            SetPreviewUnknown();
                            LogToUI($"🔎 Preview(FQDN): browse/search text '{raw}' detected; skipping exact preview.", "Debug");
                            return;
                        }

                        fqdns = raw
                            .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(10)
                            .ToList();
                    }

                    if (fqdns.Count == 0)
                    {
                        SetPreviewUnknown();
                        LogToUI("🔎 Preview(FQDN): no FQDNs found (manager empty + textbox empty).", "Debug");
                        return;
                    }

                    if (usingManagerSelection && fqdns.Count > 1)
                    {
                        var selectedFqdns = fqdns
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(10)
                            .ToList();

                        if (selectedFqdns.Count == 0)
                        {
                            SetPreviewUnknown();
                            LogToUI("⚠️ Preview(FQDN multi): selected list contained no valid values.", "Debug");
                            return;
                        }

                        var fqdnUrl = $"https://{_selectedPlatform.Url}/consumer/Devices/fqdnfiltered/{_selectedInstruction.Id}";
                        var fqdnPayloadObject = new
                        {
                            instructionDefinitionId = _selectedInstruction.Id,
                            scope = string.Join(",", selectedFqdns)
                        };
                        var fqdnPayloadJson = JsonConvert.SerializeObject(fqdnPayloadObject, Formatting.Indented);

                        LogToUI($"📡 fqdnfiltered URL: {fqdnUrl}\n");
                        LogToUI($"📤 fqdnfiltered payload:\n{fqdnPayloadJson}\n");

                        using var fqdnClient = new HttpClient();
                        fqdnClient.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                        fqdnClient.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                        using var fqdnContent = new StringContent(fqdnPayloadJson, Encoding.UTF8, "application/json");
                        var fqdnResponse = await fqdnClient.PostAsync(fqdnUrl, fqdnContent);
                        var fqdnJson = await fqdnResponse.Content.ReadAsStringAsync();

                        LogToUI($"📥 fqdnfiltered raw response:\n{fqdnJson}\n");

                        if (!fqdnResponse.IsSuccessStatusCode)
                        {
                            SetPreviewUnknown();
                            LogToUI($"❌ fqdnfiltered preview failed: {fqdnResponse.StatusCode}\n{fqdnJson}\n");
                            return;
                        }

                        var fqdnResult = JObject.Parse(fqdnJson);
                        _lastPreviewResult = fqdnResult;

                        LogToUI($"🔎 fqdnfiltered response keys: {string.Join(", ", fqdnResult.Properties().Select(p => p.Name))}\n");

                        int fqdnOnline =
                             fqdnResult["TotalDevicesOnline"]?.Value<int?>()
                             ?? fqdnResult["Online"]?.Value<int?>()
                             ?? fqdnResult["online"]?.Value<int?>()
                             ?? fqdnResult["DevicesOnline"]?.Value<int?>()
                             ?? fqdnResult["devicesOnline"]?.Value<int?>()
                             ?? 0;

                        int fqdnOffline =
                            fqdnResult["TotalDevicesOffline"]?.Value<int?>()
                            ?? fqdnResult["Offline"]?.Value<int?>()
                            ?? fqdnResult["offline"]?.Value<int?>()
                            ?? fqdnResult["DevicesOffline"]?.Value<int?>()
                            ?? fqdnResult["devicesOffline"]?.Value<int?>()
                            ?? 0;

                        int fqdnEstimated =
                            fqdnResult["TotalDevices"]?.Value<int?>()
                            ?? fqdnResult["Estimated"]?.Value<int?>()
                            ?? fqdnResult["estimated"]?.Value<int?>()
                            ?? fqdnResult["Count"]?.Value<int?>()
                            ?? fqdnResult["count"]?.Value<int?>()
                            ?? (fqdnOnline + fqdnOffline);

                        if (estimatedLabel != null)
                            estimatedLabel.Text = $"Estimated: {fqdnEstimated:N0}";

                        if (onlineLabel != null)
                            onlineLabel.Text = $"Online: {fqdnOnline:N0}";

                        if (offlineLabel != null)
                            offlineLabel.Text = $"Offline: {fqdnOffline:N0}";

                        _hasPreviewedTargets = true;
                        UpdateRunButtonState();
                        RenderBreakdownTables(fqdnResult);

                        LogToUI($"📊 Previewed selected FQDN targets: Estimated = {fqdnEstimated}, Online = {fqdnOnline}, Offline = {fqdnOffline}\n");
                        return;
                    }

                    var fqdn = fqdns[0];

                    // If someone typed a browse-style token as the only textbox value,
                    // do not pop a dialog while they are searching.
                    if (IsBrowseLikeValue(fqdn))
                    {
                        SetPreviewUnknown();
                        LogToUI($"🔎 Preview(FQDN): browse/search token '{fqdn}' detected; skipping exact preview.", "Debug");
                        return;
                    }

                    scopeQuery = JObject.FromObject(new
                    {
                        Attribute = "Fqdn",
                        Operator = "==",
                        Value = fqdn
                    });

                    LogToUI($"📦 Preview(FQDN) scope:\n{JsonConvert.SerializeObject(scopeQuery, Formatting.Indented)}", "Debug");
                }
                else if (string.Equals(targetingMode, "Coverage Tag", StringComparison.OrdinalIgnoreCase))
                {
                    string tag = _coverageTagNameComboBox?.SelectedItem?.ToString() ?? "";
                    string value = _coverageTagValueComboBox?.SelectedItem?.ToString() ?? "";

                    if (!string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(value))
                    {
                        string tagExpression = $"{tag}={value}";
                        scopeQuery = JObject.FromObject(new
                        {
                            Attribute = "TagTxt",
                            Operator = "",
                            Value = tagExpression
                        });
                    }
                    else
                    {
                        LogToUI("⚠️ Coverage Tag or Value not selected.\n");
                        return;
                    }
                }
                else if (string.Equals(targetingMode, "Management Group", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = ResolveSelectedManagementGroups();

                    if (groups.Count == 0)
                    {
                        if (estimatedLabel != null) estimatedLabel.Text = "Estimated: ?";
                        if (onlineLabel != null) onlineLabel.Text = "Online: ?";
                        if (offlineLabel != null) offlineLabel.Text = "Offline: ?";

                        _hasPreviewedTargets = false;
                        UpdateRunButtonState();

                        LogToUI("⚠️ Cannot preview targets – valid management group not selected.\n");
                        return;
                    }

                    var andRadio = TryFindControl<Avalonia.Controls.RadioButton>("MgmtGroupAndRadioButton");
                  //  bool useAnd = andRadio != null && andRadio.IsChecked == true; // missing/hidden => false => OR
                    bool useAnd = false; // temporarily force OR
                    if (groups.Count == 1)
                    {
                        var mg = groups[0];

                        if (mg.UsableId <= 0)
                        {
                            scopeQuery = _authService.IsMultiTenant
                                ? JObject.FromObject(new
                                {
                                    Attribute = "managementgroup",
                                    Operator = "=",
                                    Value = "global"
                                })
                                : new JObject();
                        }
                        else
                        {
                            scopeQuery = JObject.FromObject(new
                            {
                                Attribute = "managementgroup",
                                Operator = "=",
                                Value = mg.UsableId.ToString()
                            });
                        }
                    }
                    else
                    {
                        var usableGroups = groups.Where(g => g != null && g.UsableId > 0).ToList();

                        if (usableGroups.Count == 0)
                        {
                            scopeQuery = _authService.IsMultiTenant
                                ? JObject.FromObject(new
                                {
                                    Attribute = "managementgroup",
                                    Operator = "=",
                                    Value = "global"
                                })
                                : new JObject();
                        }
                        else
                        {
                            var operands = new JArray();

                            foreach (var mg in usableGroups)
                            {
                                operands.Add(JObject.FromObject(new
                                {
                                    Attribute = "managementgroup",
                                    Operator = "=",
                                    Value = mg.UsableId.ToString()
                                }));
                            }

                            scopeQuery = new JObject
                            {
                                ["Operator"] = useAnd ? "AND" : "OR",
                                ["Operands"] = operands
                            };
                        }
                    }

                    LogToUI($"📦 Preview(MG) scope:\n{JsonConvert.SerializeObject(scopeQuery, Formatting.Indented)}\n", "Debug");
                }
                else
                {
                    LogToUI("⚠️ Cannot preview targets – invalid or missing targeting mode.\n");
                    return;
                }

                var url = $"https://{_selectedPlatform.Url}/consumer/Devices/ApproxTarget/{_selectedInstruction.Id}";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var content = new StringContent(JsonConvert.SerializeObject(scopeQuery), Encoding.UTF8, "application/json");

                LogToUI($"📦 Preview Payload:\n{JsonConvert.SerializeObject(scopeQuery, Formatting.Indented)}\n");

                var response = await client.PostAsync(url, content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Preview API failed: {response.StatusCode}\n{json}\n");
                    return;
                }

                var result = JObject.Parse(json);
                _lastPreviewResult = result;

                int online = result["TotalDevicesOnline"]?.Value<int>() ?? 0;
                int offline = result["TotalDevicesOffline"]?.Value<int>() ?? 0;
                int estimated = online + offline;

                if (estimatedLabel != null)
                    estimatedLabel.Text = $"Estimated: {estimated:N0}";

                if (onlineLabel != null)
                    onlineLabel.Text = $"Online: {online:N0}";

                if (offlineLabel != null)
                    offlineLabel.Text = $"Offline: {offline:N0}";

                _hasPreviewedTargets = true;
                UpdateRunButtonState();
                RenderBreakdownTables(result);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error previewing targets: {ex.Message}\n");
            }
            finally
            {
                if (loadingLabel != null)
                    loadingLabel.IsVisible = false;
            }
        }
        public class ApprovalInstruction : INotifyPropertyChanged

        {
            public int Id { get; set; }
            public string InstructionName { get; set; }
            public string CreatedTimestampUtc { get; set; }
            public double TargetingPercent { get; set; }
            public int TotalOnline { get; set; }
            public int TargetedDevices { get; set; }
            public int TargetedOnline { get; set; }
            public int InstructionId { get; set; }
            public string Comment { get; set; }
            public int TotalTargeted { get; set; }


            public double TargetedOnlinePercent => TotalTargeted > 0
                ? Math.Round((double)TargetedOnline / TotalTargeted * 100, 1)
                : 0;

            public string TargetedOnlineDisplayPercent => $"{TargetedOnlinePercent:F1}%";

            public string TargetingPercentDisplay => $"{TargetingPercent:F1}%";
            public string TargetingSummary =>
                $"🌐 Total Online: {TotalOnline}  ({TargetingPercentDisplay} of ALL online Devices)\n" +
                $"📌 Targeted Online: {TargetedOnline} of 🎯 Total Targeted: {TargetedDevices} | ({OnlineTargetingPercentDisplay} of targeted)";

            public string OnlineTargetingPercentDisplay =>
                TargetedDevices > 0 ? $"{(TargetedOnline * 100.0 / TargetedDevices):F1}%" : "0%";
            public string SignalBar => GetImpactSignalBar(TargetingPercent);
            public string TargetingSeverity => GetImpactLabel(TargetingPercent);

            public IBrush SignalBrush => GetImpactBrush(TargetingPercent);
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private IBrush GetImpactBrush(double percent)
            {
                // Theme-aware dynamic brush lookup
                var app = Application.Current;
                if (app == null) return Brushes.Gray;

                if (percent >= 90)
                    return app.FindResource("ImpactHighBrush") as IBrush ?? Brushes.Red;
                if (percent >= 60)
                    return app.FindResource("ImpactMediumBrush") as IBrush ?? Brushes.OrangeRed;
                if (percent >= 30)
                    return app.FindResource("ImpactMediumLowBrush") as IBrush ?? Brushes.Orange;

                return app.FindResource("ImpactLowBrush") as IBrush ?? Brushes.Green;
            }

            private string GetImpactSignalBar(double percent)
            {
                int filled = (int)Math.Round(percent / 20); // max 5 bars
                return new string('█', filled).PadRight(5, '░');
            }

            private string GetImpactLabel(double percent)
            {
                if (percent >= 90) return "🔴 High Impact";
                if (percent >= 60) return "🟡 Medium Impact";
                if (percent >= 30) return "🟢 Medium-Low";
                return "🟢 Low Impact";
            }
        }


        private bool _isPreloadingFilters = false;
        private IBrush GetImpactBrush(string level)
        {
            var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;

            level = level.ToLowerInvariant();

            if (level.Contains("high"))
                return new SolidColorBrush(isDark ? Color.Parse("#EF5350") : Color.Parse("#D32F2F"));
            if (level.Contains("medium"))
                return new SolidColorBrush(isDark ? Color.Parse("#FFCA28") : Color.Parse("#FFA000"));
            if (level.Contains("low"))
                return new SolidColorBrush(isDark ? Color.Parse("#66BB6A") : Color.Parse("#388E3C"));

            return new SolidColorBrush(Colors.Gray);
        }


        private async Task LoadAllDataAsync()
        {
            try
            {
                // Start all tasks concurrently
                var loadInstructionsTask = LoadInstructions();
                var loadMgmtGroupsTask = LoadManagementGroupsAsync();
                var loadHistoryTask = LoadInstructionHistoryAsync();
                var loadMetricsTask = LoadExperienceMetricsAsync();  // Load experience metrics (measures)
                                                                     // var loadFiltersTask = LoadExperienceFiltersAsync(true);  // Force reload of filters

                // Wait for all tasks to complete
                //                await Task.WhenAll(loadInstructionsTask, loadMgmtGroupsTask, loadHistoryTask, loadMetricsTask, loadFiltersTask);
                await Task.WhenAll(loadInstructionsTask, loadMgmtGroupsTask, loadHistoryTask, loadMetricsTask);
                // Additional actions after all data is loaded
                LogToUI("✅ All data loaded successfully.");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error loading data: {ex.Message}");
            }
        }

        private async void OnNoDataClicked(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            await OpenOtherResponsesAsync(1);
        }

        private async void OnErrorsClicked(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            await OpenOtherResponsesAsync(2);
        }

        private async void OnNotImplementedClicked(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            await OpenOtherResponsesAsync(3);
        }
        private async Task OpenOtherResponsesAsync(int statusFilter)
        {
            if (_selectedPlatform == null || _activeResultsInstructionId <= 0)
            {
                LogToUI("❌ No active results instruction selected.\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("❌ No authentication token available.\n");
                return;
            }

            var url = $"https://{_selectedPlatform.Url}/consumer/ResponseErrors/{_activeResultsInstructionId}";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            try
            {
                var allItems = new List<OtherResponseItem>();
                string start = "0;0";
                const int pageSize = 50;

                while (true)
                {
                    var requestBody = new
                    {
                        Start = start,
                        PageSize = pageSize
                    };

                    var payloadJson = JsonConvert.SerializeObject(requestBody, Formatting.Indented);
                    using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    LogToUI($"📡 ResponseErrors URL: {url}\n");
                    LogToUI($"📤 ResponseErrors payload:\n{payloadJson}\n");

                    var response = await client.PostAsync(url, content);
                    var json = await response.Content.ReadAsStringAsync();

                    LogToUI($"📥 ResponseErrors raw response:\n{json}\n");

                    if (!response.IsSuccessStatusCode)
                    {
                        LogToUI($"❌ Failed retrieving other responses: {(int)response.StatusCode} {response.ReasonPhrase}\n{json}\n");
                        return;
                    }

                    var parsed = JToken.Parse(json);

                    var rows =
                        (parsed["Responses"] as JArray) ??
                        (parsed["Items"] as JArray) ??
                        (parsed as JArray) ??
                        new JArray();

                    if (rows.Count == 0)
                        break;

                    foreach (var r in rows)
                    {
                        int status = r["Status"]?.Value<int>() ?? 0;
                        LogToUI($"🧪 OtherResponse row: status={status}, device='{r["Fqdn"] ?? r["Device"] ?? r["DeviceName"]}', message='{r["ErrorData"] ?? r["Message"]}'\n");
                        if (status != statusFilter)
                            continue;

                        allItems.Add(new OtherResponseItem
                        {
                            Device = r["Fqdn"]?.ToString()
                                  ?? r["Device"]?.ToString()
                                  ?? r["DeviceName"]?.ToString()
                                  ?? string.Empty,

                            Message = r["ErrorData"]?.ToString()
                                   ?? r["Message"]?.ToString()
                                   ?? string.Empty,

                            Status = status,

                            ResponseTime = r["ResponseTimestampUtc"]?.Value<DateTime?>()
                                        ?? r["ResponseTime"]?.Value<DateTime?>()
                                        ?? r["CreatedTimestampUtc"]?.Value<DateTime?>()
                        });
                    }

                    // ResponseErrors paging uses a string range token, not an integer.
                    var nextStart =
                        parsed["Range"]?.ToString() ??
                        parsed["NextStart"]?.ToString();

                    if (rows.Count < pageSize || string.IsNullOrWhiteSpace(nextStart) || string.Equals(nextStart, start, StringComparison.Ordinal))
                        break;

                    start = nextStart;
                }

                LogToUI($"📊 Loaded {allItems.Count} other response row(s) for status {statusFilter}.\n");

                string title = statusFilter switch
                {
                    1 => "No Data Responses",
                    2 => "Error Responses",
                    3 => "Not Implemented Responses",
                    4 => "Response Too Large Responses",
                    _ => "Instruction Responses"
                };

                ShowOtherResponses(allItems, title);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error retrieving other responses: {ex.Message}\n");
            }
        }

        private async void ShowOtherResponses(List<OtherResponseItem> items, string title = "Instruction Responses")
        {
            string recommendedFormat = items.Count > 1_000_000 ? "tsv" : "csv";

            var formatLabel = new TextBlock
            {
                Text = "Format:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var formatComboBox = new ComboBox
            {
                Width = 120,
                SelectedIndex = recommendedFormat == "tsv" ? 1 : 0,
                ItemsSource = new List<string> { "csv", "tsv", "xlsx" }
            };

            var exportButton = new Button
            {
                Content = items.Count > 1_000_000
                    ? "Export... (TSV recommended)"
                    : "Export...",
                Margin = new Thickness(8, 0, 8, 0)
            };

            var closeButton = new Button
            {
                Content = "Close"
            };

            var topBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
        {
            formatLabel,
            formatComboBox,
            exportButton,
            closeButton
        }
            };

            var grid = new DataGrid
            {
                ItemsSource = items,
                AutoGenerateColumns = false,
                Margin = new Thickness(0, 8, 0, 0)
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Device",
                Binding = new Avalonia.Data.Binding("Device"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Response Time",
                Binding = new Avalonia.Data.Binding("ResponseTime"),
                Width = new DataGridLength(1.75, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Message",
                Binding = new Avalonia.Data.Binding("Message"),
                Width = new DataGridLength(4, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Status",
                Binding = new Avalonia.Data.Binding("StatusText"),
                Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
            });

            var root = new DockPanel
            {
                LastChildFill = true
            };

            DockPanel.SetDock(topBar, Dock.Top);
            root.Children.Add(topBar);
            root.Children.Add(grid);

            var window = new Window
            {
                Title = title,
                Width = 1100,
                Height = 650,
                Content = root
            };

            exportButton.Click += async (_, __) =>
            {
                string format = formatComboBox.SelectedItem?.ToString() ?? recommendedFormat;

                if (items.Count > 1_000_000 && !string.Equals(format, "tsv", StringComparison.OrdinalIgnoreCase))
                {
                    LogToUI("⚠️ For exports over 1,000,000 rows, TSV is required.\n");
                    return;
                }

                await ExportOtherResponsesWithPromptAsync(items, format);
            };

            closeButton.Click += (_, __) => window.Close();

            await window.ShowDialog(this);
        }

        private static string EscapeDelimited(string? value, char delimiter)
        {
            value ??= string.Empty;

            bool mustQuote =
                value.Contains(delimiter) ||
                value.Contains('"') ||
                value.Contains('\r') ||
                value.Contains('\n');

            if (value.Contains('"'))
                value = value.Replace("\"", "\"\"");

            return mustQuote ? $"\"{value}\"" : value;
        }

        private async Task ExportOtherResponsesDelimitedAsync(List<OtherResponseItem> items, char delimiter, string fullPath)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Join(delimiter, new[]
            {
        "Device",
        "ResponseTime",
        "Message",
        "Status"
    }));

            foreach (var item in items)
            {
                sb.AppendLine(string.Join(delimiter, new[]
                {
            EscapeDelimited(item.Device, delimiter),
            EscapeDelimited(item.ResponseTime?.ToString("yyyy-MM-dd HH:mm:ss"), delimiter),
            EscapeDelimited(item.Message, delimiter),
            EscapeDelimited(item.StatusText, delimiter)
        }));
            }

            await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8);
        }

        private async Task ExportOtherResponsesXlsxAsync(List<OtherResponseItem> items, string fullPath)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Other Responses");

            ws.Cell(1, 1).Value = "Device";
            ws.Cell(1, 2).Value = "ResponseTime";
            ws.Cell(1, 3).Value = "Message";
            ws.Cell(1, 4).Value = "Status";

            for (int i = 0; i < items.Count; i++)
            {
                var row = i + 2;
                var item = items[i];

                ws.Cell(row, 1).Value = item.Device;
                ws.Cell(row, 2).Value = item.ResponseTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                ws.Cell(row, 3).Value = item.Message;
                ws.Cell(row, 4).Value = item.StatusText;
            }

            ws.Columns().AdjustToContents();

            workbook.SaveAs(fullPath);
        }

        private async Task<string?> PromptForOtherResponsesSavePathAsync(string defaultExtension, string defaultFileName)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return null;

            var fileTypeChoices = new List<FilePickerFileType>();

            if (string.Equals(defaultExtension, "csv", StringComparison.OrdinalIgnoreCase))
            {
                fileTypeChoices.Add(new FilePickerFileType("CSV File")
                {
                    Patterns = new[] { "*.csv" },
                    MimeTypes = new[] { "text/csv" }
                });
            }
            else if (string.Equals(defaultExtension, "tsv", StringComparison.OrdinalIgnoreCase))
            {
                fileTypeChoices.Add(new FilePickerFileType("TSV File")
                {
                    Patterns = new[] { "*.tsv" },
                    MimeTypes = new[] { "text/tab-separated-values", "text/plain" }
                });
            }
            else if (string.Equals(defaultExtension, "xlsx", StringComparison.OrdinalIgnoreCase))
            {
                fileTypeChoices.Add(new FilePickerFileType("Excel Workbook")
                {
                    Patterns = new[] { "*.xlsx" },
                    MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
                });
            }

            var suggested = defaultFileName.EndsWith("." + defaultExtension, StringComparison.OrdinalIgnoreCase)
                ? defaultFileName
                : $"{defaultFileName}.{defaultExtension}";

            var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Other Responses Export",
                SuggestedFileName = suggested,
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices,
                ShowOverwritePrompt = true
            });

            return result?.TryGetLocalPath();
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Replace(' ', '_');
        }

        private async Task ExportOtherResponsesWithPromptAsync(List<OtherResponseItem> items, string format)
        {
            try
            {
                if (items == null || items.Count == 0)
                {
                    LogToUI("ℹ️ No other responses to export.\n");
                    return;
                }

                string normalizedFormat = format.Trim().ToLowerInvariant();
                string instructionName = _selectedInstruction?.Name ?? "Instruction";
                instructionName = SanitizeFileName(instructionName);

                string responseId = _activeResultsInstructionId.ToString();

                string responseType = items.FirstOrDefault()?.Status switch
                {
                    1 => "Success_NoContent",
                    2 => "Errors",
                    3 => "NotImplemented",
                    4 => "ResponseTooLarge",
                    _ => "Responses"
                };

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                string baseFileName = $"{instructionName}_{responseId}_{responseType}_{timestamp}";

                var savePath = await PromptForOtherResponsesSavePathAsync(normalizedFormat, baseFileName);
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    LogToUI("ℹ️ Export cancelled.\n");
                    return;
                }

                if (normalizedFormat == "csv")
                {
                    await ExportOtherResponsesDelimitedAsync(items, ',', savePath);
                }
                else if (normalizedFormat == "tsv")
                {
                    await ExportOtherResponsesDelimitedAsync(items, '\t', savePath);
                }
                else if (normalizedFormat == "xlsx")
                {
                    await ExportOtherResponsesXlsxAsync(items, savePath);
                }
                else
                {
                    LogToUI($"❌ Unsupported export format: {format}\n");
                    return;
                }

                LogToUI($"✅ Exported other responses to: {savePath}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to export other responses: {ex.Message}\n");
            }
        }

        private async Task ExportOtherResponsesAsync(List<OtherResponseItem> items, char delimiter, string extension)
        {
            try
            {
                var downloads = System.IO.Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
              "Downloads");

                Directory.CreateDirectory(downloads);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"OtherResponses_{timestamp}.{extension}";
                var fullPath = System.IO.Path.Combine(downloads, fileName);

                var sb = new StringBuilder();

                sb.AppendLine(string.Join(delimiter, new[]
                {
            "Device",
            "ResponseTime",
            "Message",
            "Status"
        }));

                foreach (var item in items)
                {
                    sb.AppendLine(string.Join(delimiter, new[]
                    {
                EscapeDelimited(item.Device, delimiter),
                EscapeDelimited(item.ResponseTime?.ToString("yyyy-MM-dd HH:mm:ss"), delimiter),
                EscapeDelimited(item.Message, delimiter),
                EscapeDelimited(item.Status.ToString(), delimiter)
            }));
                }

                await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8);

                LogToUI($"✅ Exported other responses to: {fullPath}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to export other responses: {ex.Message}\n");
            }
        }

        private async Task LoadExperienceMetricsAsync()
        {
            try
            {
                // Fetch experience metrics for the selected platform
                _experienceMeasures = await _measureService.GetExperienceMeasuresAsync(_selectedPlatform.Url, _token);

                LogToUI($"📊 Loaded {_experienceMeasures.Count} measures.");

                // Populate the ListBox with the fetched metrics
                //PopulateMeasuresListBox();
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load metrics: {ex.Message}");
            }
        }


        private Dictionary<string, List<string>> _filterCache = new Dictionary<string, List<string>>();
        private bool _filtersLoaded = false; // Track whether filters are already loaded


        private Dictionary<string, Dictionary<string, List<string>>> _filterCacheByPlatform = new Dictionary<string, Dictionary<string, List<string>>>();

        private async Task<List<string>> GetFiltersFromCacheOrFetchAsync(string filterType, string platformUrl, string token)
        {
            // Ensure ExperienceService is initialized
            if (_experienceService == null)
            {
                LogToUI("❌ ExperienceService is not initialized.");
                return new List<string>();
            }

            if (string.IsNullOrEmpty(platformUrl))
            {
                LogToUI("❌ Platform URL is not provided.");
                return new List<string>();
            }

            // Fetch filters directly from the API, ignoring cache
            List<string> result = filterType switch
            {
                "ManagementGroup" => await _experienceService.GetManagementGroupsAsync(platformUrl, _token),
                "Location" => await _experienceService.GetLocationsAsync(platformUrl, _token),
                "OperatingSystem" => await _experienceService.GetOperatingSystemsAsync(platformUrl, _token),
                "DeviceModel" => await _experienceService.GetDeviceModelsAsync(platformUrl, _token),
                "Criticality" => await _experienceService.GetCriticalitiesAsync(platformUrl, _token),
                _ => new List<string>() // Return an empty list for invalid filterType
            };

            // Log the fetched result
            if (result != null && result.Any())
            {
                LogToUI($"✅ Fetched {filterType} from the API: {result.Count} values for platform {platformUrl}");
            }
            else
            {
                LogToUI($"⚠️ No data fetched for {filterType} on platform {platformUrl}");
            }

            return result;
        }




        private void ClearFiltersCache()
        {
            _filterCache.Clear(); // Clear the cache
            _filtersLoaded = false; // Reset the loaded flag
            LogToUI("✅ Filters cache cleared.");
        }


        private async Task<HttpResponseMessage> FetchWithRetryAsync(HttpClient client, string url, StringContent content, int maxRetries = 3)
        {
            int attempt = 0;
            while (attempt < maxRetries)
            {
                try
                {
                    return await client.PostAsync(url, content);
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    attempt++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    LogToUI($"❌ Request timed out. Retrying in {delay.TotalSeconds}s... (Attempt {attempt})");
                    await Task.Delay(delay);
                }
            }
            throw new Exception("Maximum retry attempts reached. Request failed.");
        }
        /*

        private async Task LoadExperienceFiltersAsync(bool forceReload = false)
        {
            try
            {
                // Always fetch filters from the API (bypassing cache)
                var mgListTask = GetFiltersFromCacheOrFetchAsync("ManagementGroup", _selectedPlatform?.Url, _token);
                var locationListTask = GetFiltersFromCacheOrFetchAsync("Location", _selectedPlatform?.Url, _token);
                var modelListTask = GetFiltersFromCacheOrFetchAsync("DeviceModel", _selectedPlatform?.Url, _token);
                var osListTask = GetFiltersFromCacheOrFetchAsync("OperatingSystem", _selectedPlatform?.Url, _token);
                var criticalityListTask = GetFiltersFromCacheOrFetchAsync("Criticality", _selectedPlatform?.Url, _token);

                // Wait for all tasks to complete
                await Task.WhenAll(mgListTask, locationListTask, modelListTask, osListTask, criticalityListTask);

                // Set the ItemsSource for each ListBox control
                ExperienceMgmtGroupListBox.ItemsSource = mgListTask.Result;
                ExperienceLocationListBox.ItemsSource = locationListTask.Result;
                ExperienceModelListBox.ItemsSource = modelListTask.Result;
                ExperienceOsListBox.ItemsSource = osListTask.Result;
                ExperienceCriticalityListBox.ItemsSource = criticalityListTask.Result;

                LogToUI("✅ Filters loaded successfully from the API.");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed loading filters: {ex.Message}");
            }
        }

        */

        private List<string> GetSelectedItems(ListBox listBox)
        {
            if (listBox == null)
                return new List<string>();

            return listBox.SelectedItems?.Cast<string>().ToList() ?? new List<string>();
        }


        private async void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var tabControl = sender as TabControl ?? TryFindControl<TabControl>("MainTabControl");
            if (tabControl == null)
                return;

            int selectedIndex = tabControl.SelectedIndex;
            LogToUI($"Tab changed to index: {selectedIndex}");

            bool isInstructionDetailTab = selectedIndex == 0;

            // The target builder now lives inside Instruction Detail,
            // so do not force-hide or force-show the selected-targets pane here.
            if (isInstructionDetailTab)
            {
                LogToUI("✅ Checking if filters need to be loaded...");

                if (!_experienceFiltersLoaded)
                {
                    LogToUI("✅ Filters not loaded, starting to load...");
                    // await LoadExperienceFiltersAsync(forceReload: true);
                    LogToUI("✅ Filters should be loaded.");
                }
                else
                {
                    LogToUI("✅ Filters are already loaded.");
                }
            }
            else
            {
                LogToUI($"Not switching to Experience tab. Currently at index: {selectedIndex}");
            }

            await Task.CompletedTask;
        }





        private async void OnPreviewTargetsClicked(object sender, RoutedEventArgs e)
        {
            LogToUI("🧪 ENTERED OnPreviewTargetsClicked\n");
            if (_selectedInstruction == null)
            {
                LogToUI("❌ No instruction selected for preview.\n");
                return;
            }

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("❌ No platform selected for preview.\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("❌ No authentication token available for preview.\n");
                return;
            }

            var estimatedLabel = this.FindControl<TextBlock>("EstimatedCountLabel");
            var onlineLabel = this.FindControl<TextBlock>("OnlineCountLabel");
            var offlineLabel = this.FindControl<TextBlock>("OfflineCountLabel");

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            try
            {
                var targetingMode = GetSelectedTargetingMode();

                // If user explicitly selected multiple FQDNs in the manager,
                // use the dedicated fqdnfiltered preview endpoint instead of ApproxTarget.
                if (string.Equals(targetingMode, "FQDN", StringComparison.OrdinalIgnoreCase) &&
                    _fqdnManagerSelectedItems != null &&
                    _fqdnManagerSelectedItems.Count > 1)
                {
                    var selectedFqdns = _fqdnManagerSelectedItems
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToList();

                    if (selectedFqdns.Count == 0)
                    {
                        LogToUI("❌ No valid FQDNs selected for preview.\n");
                        return;
                    }

                    var fqdnPayloadObject = new
                    {
                        instructionDefinitionId = _selectedInstruction.Id,
                        scope = string.Join(",", selectedFqdns)
                    };

                    var fqdnPayloadJson = JsonConvert.SerializeObject(fqdnPayloadObject, Formatting.Indented);
                    var fqdnUrl = $"https://{_selectedPlatform.Url}/consumer/Devices/fqdnfiltered/{_selectedInstruction.Id}";

                    LogToUI($"🔍 Previewing {selectedFqdns.Count} selected FQDN(s) using fqdnfiltered.\n");

                    var json = await ApiLogger.LogApiCallAsync(
                        label: "PreviewTargetsFqdnFiltered",
                        endpoint: $"consumer/Devices/fqdnfiltered/{_selectedInstruction.Id}",
                        apiCall: async () =>
                        {
                            using var payload = new StringContent(fqdnPayloadJson, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(fqdnUrl, payload);
                            var resultJson = await response.Content.ReadAsStringAsync();

                            if (!response.IsSuccessStatusCode)
                                throw new Exception($"Preview FQDN filter failed: {(int)response.StatusCode} {response.ReasonPhrase} - {resultJson}");

                            return resultJson;
                        },
                        payloadJson: fqdnPayloadJson

                    );
                    LogToUI($"📡 fqdnfiltered URL: {fqdnUrl}\n");
                    LogToUI($"📤 fqdnfiltered payload:\n{fqdnPayloadJson}\n");
                    LogToUI($"📥 fqdnfiltered raw response:\n{json}\n");
                    var result = JObject.Parse(json);

                    int online =
                        result["TotalDevicesOnline"]?.Value<int?>()
                        ?? result["Online"]?.Value<int?>()
                        ?? result["online"]?.Value<int?>()
                        ?? 0;

                    int offline =
                        result["TotalDevicesOffline"]?.Value<int?>()
                        ?? result["Offline"]?.Value<int?>()
                        ?? result["offline"]?.Value<int?>()
                        ?? 0;

                    int estimated =
                        result["TotalDevices"]?.Value<int?>()
                        ?? result["Estimated"]?.Value<int?>()
                        ?? result["estimated"]?.Value<int?>()
                        ?? (online + offline);

                    if (estimatedLabel != null)
                        estimatedLabel.Text = $"Estimated: {estimated:N0}";

                    if (onlineLabel != null)
                        onlineLabel.Text = $"Online: {online:N0}";

                    if (offlineLabel != null)
                        offlineLabel.Text = $"Offline: {offline:N0}";

                    _hasPreviewedTargets = true;
                    UpdateRunButtonState();

                    LogToUI($"📊 Previewed selected FQDN targets: Estimated = {estimated}, Online = {online}, Offline = {offline}\n");
                    return;
                }

                // Default behavior for MG / Coverage / single FQDN / other dynamic scopes
                var scopeQuery = BuildDynamicScopeExpression();
                var url = $"https://{_selectedPlatform.Url}/consumer/Devices/ApproxTarget/{_selectedInstruction.Id}";
                var payloadJson = scopeQuery.ToString();

                var jsonApprox = await ApiLogger.LogApiCallAsync(
                    label: "PreviewTargets",
                    endpoint: $"consumer/Devices/ApproxTarget/{_selectedInstruction.Id}",
                    apiCall: async () =>
                    {
                        using var payload = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(url, payloadJson == null ? null : payload);
                        var resultJson = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                            throw new Exception($"Preview failed: {(int)response.StatusCode} {response.ReasonPhrase} - {resultJson}");

                        return resultJson;
                    },
                    payloadJson: payloadJson
                );

                var resultApprox = JObject.Parse(jsonApprox);

                int onlineApprox = resultApprox["TotalDevicesOnline"]?.Value<int>() ?? 0;
                int offlineApprox = resultApprox["TotalDevicesOffline"]?.Value<int>() ?? 0;
                int estimatedApprox = onlineApprox + offlineApprox;

                if (estimatedLabel != null)
                    estimatedLabel.Text = $"Estimated: {estimatedApprox:N0}";

                if (onlineLabel != null)
                    onlineLabel.Text = $"Online: {onlineApprox:N0}";

                if (offlineLabel != null)
                    offlineLabel.Text = $"Offline: {offlineApprox:N0}";

                _hasPreviewedTargets = true;
                UpdateRunButtonState();

                LogToUI($"📊 Previewed targets: Estimated = {estimatedApprox}, Online = {onlineApprox}, Offline = {offlineApprox}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error previewing targets: {ex.Message}\n");
            }
        }

        private void RenderBreakdownTables(JObject result)
        {
            var breakdownPanel = this.FindControl<StackPanel>("BreakdownPanel");
            if (breakdownPanel == null)
            {
                LogToUI("⚠️ BreakdownPanel not found in visual tree.\n");
                return;
            }

            breakdownPanel.Children.Clear();

            var modeToggle = this.FindControl<ToggleSwitch>("BreakdownModeToggle");
            bool showOsBreakdown = modeToggle?.IsChecked ?? true;

            if (showOsBreakdown)
            {
                if (result["ByOsType"] is JArray osTypes && osTypes.Count > 0)
                {
                    var osHeader = new TextBlock
                    {
                        Text = "📊 By OS:",
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(0, 4, 0, 2)
                    };
                    breakdownPanel.Children.Add(osHeader);

                    foreach (var item in osTypes)
                    {
                        string os = item["AggregateValue"]?.ToString() ?? "Unknown";
                        int online = item["CountOnline"]?.Value<int>() ?? 0;
                        int offline = item["CountOffline"]?.Value<int>() ?? 0;

                        breakdownPanel.Children.Add(new TextBlock
                        {
                            Text = $"{os}: 🟢 {online:N0}  🔴 {offline:N0}",
                            Margin = new Thickness(8, 2, 0, 2)
                        });
                    }
                }
                else
                {
                    breakdownPanel.Children.Add(new TextBlock
                    {
                        Text = "No OS breakdown data available.",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(8, 4, 0, 0)
                    });
                }
            }
            else
            {
                if (result.TryGetValue("ByDeviceType", out var deviceArray) && deviceArray is JArray deviceTypes)

                {
                    var deviceHeader = new TextBlock
                    {
                        Text = "📊 By Device Type:",
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(0, 4, 0, 2)
                    };
                    breakdownPanel.Children.Add(deviceHeader);

                    foreach (var item in deviceTypes)
                    {
                        string deviceType = item["AggregateValue"]?.ToString() ?? "Unknown";
                        int online = item["CountOnline"]?.Value<int>() ?? 0;
                        int offline = item["CountOffline"]?.Value<int>() ?? 0;

                        breakdownPanel.Children.Add(new TextBlock
                        {
                            Text = $"{deviceType}: 🟢 {online:N0}  🔴 {offline:N0}",
                            Margin = new Thickness(8, 2, 0, 2)
                        });
                    }
                }
                else
                {
                    breakdownPanel.Children.Add(new TextBlock
                    {
                        Text = "No device type breakdown data available.",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(8, 4, 0, 0)
                    });
                }
            }
        }




        private void UpdateRunButtonState()
        {
            var runButton = this.FindControl<Button>("RunButton");
            var targetingMode = _targetingModeComboBox.SelectedItem?.ToString();

            if (targetingMode == "Dynamic")
                runButton.IsEnabled = _hasPreviewedTargets;
            else
                runButton.IsEnabled = true;
        }


        private async void OnAddFilterClicked(object sender, RoutedEventArgs e)
        {
            var attributeCombo = this.FindControl<ComboBox>("FilterAttributeComboBox");
            var operatorCombo = this.FindControl<ComboBox>("FilterOperatorComboBox");
            var valueCombo = this.FindControl<ComboBox>("FilterValueComboBox");
            var manufacturerTextBox = this.FindControl<TextBox>("FilterValueTextBox");  // Example for manual input

            // Capture the selected values from the ComboBoxes
            var attribute = attributeCombo?.SelectedItem?.ToString();
            var op = operatorCombo?.SelectedItem?.ToString() ?? ""; // Ensure op has a value
            var value = valueCombo?.SelectedItem?.ToString() ?? "";
            var manualValue = manufacturerTextBox?.Text?.Trim();  // Capture manual value from TextBox

            // Log the selected values
            LogToUI($"🔍 Selected Attribute: {attribute}\n");
            LogToUI($"🔍 Selected Operator: {op}\n");
            LogToUI($"🔍 Selected Value: {value}\n");
            LogToUI($"🔍 Manual Value (if any): {manualValue}\n"); // Log the manual value (e.g., Manufacturer)

            // If any of the values are invalid, return early and don't add the filter
            if (string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(op) ||
                (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(manualValue)))
            {
                LogToUI("⚠️ Invalid filter. All fields required.\n");
                return;
            }

            var group = _filterGroups.LastOrDefault();
            if (group == null)
            {
                LogToUI("⚠️ No filter group found.\n");
                return;
            }

            // Log the current filter group and its filters
            LogToUI($"🔍 Current Filter Group Logic: {group.Logic}\n");
            LogToUI($"🔍 Existing Filters in Group: {group.Filters.Count}\n");

            if (group.Filters.Count > 0)
            {
                group.Filters[group.Filters.Count - 1].LogicAfter = group.Logic;
            }

            // Use the manual value if it's filled, otherwise use the value from the ComboBox
            var finalValue = !string.IsNullOrWhiteSpace(manualValue) ? manualValue : value;

            // Adjust the operator for special cases
            if (op == "Contains")
            {
                op = "LIKE";
                finalValue = $"%{finalValue}%"; // Apply wildcards for Contains
            }
            else if (op == "Begins with")
            {
                op = "LIKE";
                finalValue = $"{finalValue}%"; // Apply wildcard only at the end for Begins with
            }
            else if (op == "Ends with")
            {
                op = "LIKE";
                finalValue = $"%{finalValue}"; // Apply wildcard only at the beginning for Ends with
            }

            // Log the operator change and final value used for the filter
            LogToUI($"🔍 Final Operator: {op}\n");
            LogToUI($"🔍 Final Raw Value for API: {finalValue}\n");

            // Add the new filter expression
            var newFilter = new FilterExpression
            {
                Attribute = attribute,
                Operator = op,  // Use the updated operator (like LIKE instead of Contains, Begins with, Ends with)
                Value = finalValue,
                LogicAfter = null
            };

            LogToUI($"✔️ Adding New Filter: {JsonConvert.SerializeObject(newFilter)}\n");

            group.Filters.Add(newFilter);

            // Log the updated filter group
            LogToUI($"🔍 Updated Filter Group: {JsonConvert.SerializeObject(group)}\n");

            //UpdateChipPanel(group);

            // Clear the ComboBox and TextBox selections
            attributeCombo.SelectedIndex = -1;
            operatorCombo.SelectedIndex = -1;
            valueCombo.SelectedIndex = -1;
            manufacturerTextBox.Clear();  // Clear the manual text input

            await UpdatePreviewAsync(); // ✅ Trigger live preview
        }



        private void AddCoverageGroup(object sender, RoutedEventArgs e)

        {
            var attributeCombo = this.FindControl<ComboBox>("FilterAttributeComboBox");
            var operatorCombo = this.FindControl<ComboBox>("FilterOperatorComboBox");
            var valueCombo = this.FindControl<ComboBox>("FilterValueComboBox");


            var attribute = attributeCombo.SelectedItem?.ToString();
            var op = operatorCombo.SelectedItem?.ToString();
            var value = valueCombo.SelectedItem?.ToString();

            if (string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(op) || string.IsNullOrWhiteSpace(value))
            {
                // Optionally show error or prevent add
                return;
            }

            var group = new FilterGroup
            {
                Logic = "AND",
                Filters = new List<FilterExpression>
        {
            new FilterExpression { Attribute = attribute, Operator = op, Value = value }
        }
            };

            _filterGroups.Add(group);
            attributeCombo.SelectedIndex = -1;
            operatorCombo.SelectedIndex = -1;
            valueCombo.SelectedIndex = -1;

            //RenderFilterGroups();
        }
        private void AddCoverageClicked(object sender, RoutedEventArgs e)
        {
            var group = new FilterGroup
            {
                Logic = "AND",
                Filters = new List<FilterExpression>()
            };

            _filterGroups.Add(group);
            //  RenderFilterGroups();
        }



        public class FilterExpression
        {
            public string Attribute { get; set; } = "";
            public string Operator { get; set; } = "";
            public string Value { get; set; } = "";
            public string LogicAfter { get; set; } = "";  // "AND" or "OR"
        }
        /*
        private void RenderFilterGroups()
        {
            var container = this.FindControl<StackPanel>("FilterGroupContainer");
            container.Children.Clear();

            for (int i = 0; i < _filterGroups.Count; i++)
            {
                var group = _filterGroups[i];

                // ✅ Always show group logic ComboBox (not just when i > 0)
                var groupLogicDropdown = new ComboBox
                {
                    ItemsSource = new List<string> { "AND", "OR" },
                    SelectedItem = group.Logic,
                    MinWidth = 80,
                    MaxWidth = 150,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4)
                };

                groupLogicDropdown.SelectionChanged += (_, __) =>
                {
                    group.Logic = groupLogicDropdown.SelectedItem?.ToString() ?? "AND";
                };

                container.Children.Add(groupLogicDropdown);

                // Group container
                var border = new Border
                {
                    BorderBrush = Brushes.SlateGray,
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#2f4f4f")),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var groupPanel = new StackPanel { Orientation = Orientation.Vertical };

                // Chips row
                var chipRow = new WrapPanel
                {
                    Margin = new Thickness(0, 4, 0, 0)
                };

                for (int j = 0; j < group.Filters.Count; j++)
                {
                    var filter = group.Filters[j];

                    var chip = BuildFilterChip(filter, () =>
                    {
                        group.Filters.Remove(filter);
                        RenderFilterGroups();
                    });

                    chip.Margin = new Thickness(4, 2, 4, 2);
                    chipRow.Children.Add(chip);

                    if (j < group.Filters.Count - 1)
                    {
                        var logicCombo = new ComboBox
                        {
                            ItemsSource = new List<string> { "AND", "OR" },
                            SelectedItem = filter.LogicAfter ?? "AND",
                            Width = 80,
                            Margin = new Thickness(4, 0, 4, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        int capturedIndex = j;
                        logicCombo.SelectionChanged += (_, __) =>
                        {
                            group.Filters[capturedIndex].LogicAfter = logicCombo.SelectedItem?.ToString() ?? "AND";
                            RenderFilterGroups();
                        };

                        chipRow.Children.Add(logicCombo);
                    }
                }

                // Optional: show placeholder operator if only 1 filter (visual consistency)
                if (group.Filters.Count == 1)
                {
                    var dummyLogic = new ComboBox
                    {
                        ItemsSource = new List<string> { "AND", "OR" },
                        SelectedItem = group.Logic,
                        Width = 80,
                        IsEnabled = false,
                        Margin = new Thickness(4, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    chipRow.Children.Add(dummyLogic);
                }

                groupPanel.Children.Add(chipRow);

                // Remove Group button
                if (_filterGroups.Count > 1)
                {
                    var removeGroupButton = new Button
                    {
                        Content = "❌ Remove Group",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 4, 0, 0),
                        FontSize = 12,
                        Padding = new Thickness(6, 2)
                    };

                    removeGroupButton.Click += (_, __) =>
                    {
                        _filterGroups.Remove(group);
                        RenderFilterGroups();
                    };

                    groupPanel.Children.Add(removeGroupButton);
                }

                border.Child = groupPanel;
                container.Children.Add(border);

                _filterGroupToPanelMap[group] = chipRow;
            }

            // ➕ Add Group button
            var addGroupButton = new Button
            {
                Content = "➕ Add Group",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12,
                Padding = new Thickness(6, 2)
            };

            addGroupButton.Click += AddCoverageClicked;
            container.Children.Add(addGroupButton);
        }


        */


        public static class VersionService
        {
            public static async Task<bool> IsLegacyVersionAsync(string platformUrl, string token)
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);
                    var response = await client.GetStringAsync($"https://{platformUrl}/consumer/information");

                    var obj = JObject.Parse(response);
                    var version = obj["Version"]?.ToString() ?? "";

                    // Treat versions < 25.0.0 as legacy
                    return version.StartsWith("9.") || version.StartsWith("8.") || version.StartsWith("7.");
                }
                catch
                {
                    return false; // fallback to non-legacy
                }
            }
        }
        public class FilterCondition
        {
            public string Attribute { get; set; } = "";
            public string Operator { get; set; } = "";
            public string Value { get; set; } = "";
        }

        public class FilterGroup
        {
            public string Logic { get; set; } = "AND"; // logic between conditions
            public List<FilterCondition> Conditions { get; set; } = new();
            public List<FilterExpression> Filters { get; set; } = new();
        }




        private void AddCoverageGroup()
        {
            var group = new FilterGroup
            {
                Logic = "AND",
                Filters = new List<FilterExpression>() // Initially empty
            };

            _filterGroups.Add(group);

            // Inner panel for content
            var groupPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 8, 0, 8)
            };

            groupPanel.Children.Add(new TextBlock
            {
                Text = "Group Logic:",
                FontSize = 12,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var logicCombo = new ComboBox
            {
                ItemsSource = new List<string> { "AND", "OR" },
                SelectedItem = group.Logic,
                Width = 80,
                Margin = new Thickness(0, 0, 0, 6)
            };

            logicCombo.SelectionChanged += (_, __) =>
            {
                group.Logic = logicCombo.SelectedItem?.ToString() ?? "AND";
            };

            groupPanel.Children.Add(logicCombo);

            var chipPanel = new WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 0)
            };

            groupPanel.Children.Add(chipPanel);

            _filterGroupToPanelMap[group] = chipPanel;

            // Wrap in border for visual and padding
            var border = new Border
            {
                Background = new SolidColorBrush(AvaloniaColor.Parse("#2f4f4f")),
                BorderBrush = Brushes.SlateGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Child = groupPanel
            };

            this.FindControl<StackPanel>("FilterGroupContainer").Children.Add(border);
        }

        /*
        private void UpdateChipPanel(FilterGroup group)
        {
            if (!_filterGroupToPanelMap.TryGetValue(group, out var chipPanel))
                return;

            chipPanel.Children.Clear();

            for (int j = 0; j < group.Filters.Count; j++)
            {
                var filter = group.Filters[j];

                var chip = BuildFilterChip(filter, () =>
                {
                    group.Filters.Remove(filter);
                    UpdateChipPanel(group); // re-render that group only
                });

                chip.Margin = new Thickness(4, 2, 4, 2);
                chipPanel.Children.Add(chip);

                if (j < group.Filters.Count - 1)
                {
                    var logicLabel = new TextBlock
                    {
                        Text = group.Filters[j].LogicAfter ?? "AND",  // per-filter logic
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 6, 0)
                    };

                    chipPanel.Children.Add(logicLabel);
                }
            }
        }

        */

        private async Task LoadCustomPropertiesAsync()
        {
            if (string.IsNullOrEmpty(_token) || _selectedPlatform == null)
                return;

            string url = $"https://{_selectedPlatform.Url}/Consumer/CustomProperties/Search";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

            try
            {
                var requestBody = new
                {
                    start = 1,
                    pageSize = 50,
                    sort = new object[] { },
                    Filter = new
                    {
                        Attribute = "TypeName",
                        Operator = "==",
                        Value = "CoverageTag"
                    }
                };

                var jsonBody = JsonConvert.SerializeObject(requestBody);
                var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var responseContent = await ApiLogger.LogApiCallAsync(
                    label: "CustomPropertiesSearch",
                    endpoint: "Consumer/CustomProperties/Search",
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync(url, httpContent);
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: jsonBody
                );

                JObject result = JObject.Parse(responseContent);
                JArray items = result["Items"] as JArray ?? new JArray();

                // Coverage tags (CustomProperties) should add to the dynamic filter system, not replace it.
                // Populate the CoverageTag dropdown in one shot after we finish parsing.
                var coverageTagAttributes = new List<string>();
                _dynamicAttributes.Clear();

                foreach (var item in items)
                {
                    string? name = item["Name"]?.ToString();
                    var values = item["Values"]
                        ?.Select(v => v["Value"]?.ToString())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct()
                        .ToList() ?? new List<string>();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    string fullAttribute = $"CoverageTags.{name}";

                    // Keep both key forms for lookup convenience (some call sites use CoverageTags.X, others use X)
                    _attributeValueMap[fullAttribute] = values;
                    _attributeValueMap[name] = values;

                    _dynamicAttributes.Add(fullAttribute);
                    coverageTagAttributes.Add(fullAttribute);

                    LogToUI($"✅ Custom Tag: {name} => [{string.Join(", ", values)}]\n");
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (_coverageTagNameComboBox != null)
                    {
                        _coverageTagNameComboBox.ItemsSource = coverageTagAttributes
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (_coverageTagNameComboBox.SelectedItem == null && _coverageTagNameComboBox.ItemCount > 0)
                            _coverageTagNameComboBox.SelectedIndex = 0;
                    }

                    // Merge CoverageTags.* into the dynamic filter attribute dropdown without clobbering existing items.
                    var existing = (_filterAttributeComboBox.ItemsSource as System.Collections.IEnumerable)
                        ?.Cast<object>()
                        .Select(x => x?.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList()
                        ?? new List<string>();

                    var merged = existing
                        .Concat(coverageTagAttributes)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    _filterAttributeComboBox.ItemsSource = merged;

                    if (_filterAttributeComboBox.SelectedItem == null && merged.Count > 0)
                        _filterAttributeComboBox.SelectedIndex = 0;
                });
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error loading custom properties: {ex.Message}\n");
            }
        }


        private void AddFilterChip()
        {
            var attr = _filterAttributeComboBox.SelectedItem?.ToString() ?? "";
            var op = _filterOperatorComboBox.SelectedItem?.ToString() ?? "";
            var value = _filterValueComboBox.IsVisible
                ? _filterValueComboBox.SelectedItem?.ToString() ?? ""
                : _filterValueTextBox?.Text ?? "";

            // Log the selected values
            LogToUI($"🔍 Selected Attribute: {attr}\n");
            LogToUI($"🔍 Selected Operator: {op}\n");
            LogToUI($"🔍 Selected Value: {value}\n");

            if (string.IsNullOrWhiteSpace(attr) || string.IsNullOrWhiteSpace(op) || string.IsNullOrWhiteSpace(value))
            {
                LogToUI("⚠️ Invalid filter. All fields required.\n");
                return;
            }

            var filterAttr = _allAttributes.FirstOrDefault(a => a.Name == attr);
            var apiAttr = filterAttr?.ApiName ?? attr;

            // Log the API attribute (apiAttr)
            LogToUI($"🔍 API Attribute: {apiAttr}\n");

            // Get raw value based on AllowedValues or default to the typed value
            var rawValue = filterAttr?.AllowedValues?.FirstOrDefault(v => v.StartsWith(value + " | "))?.Split('|')[1].Trim() ?? value;

            // Log the raw value
            LogToUI($"🔍 Raw Value: {rawValue}\n");

            // Adjust the operator for special cases
            if (op == "Contains") { op = "like"; rawValue = $"%{rawValue}%"; }
            else if (op == "Begins with") { op = "like"; rawValue = $"{rawValue}%"; }
            else if (op == "Ends with") { op = "like"; rawValue = $"%{rawValue}"; }

            // Log the operator change if necessary
            LogToUI($"🔍 Final Operator: {op}\n");
            LogToUI($"🔍 Final Raw Value for API: {rawValue}\n");

            var filterText = $"{attr} {op} {value}";

            var chipPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
        {
            new TextBlock { Text = filterText, VerticalAlignment = VerticalAlignment.Center },
            new Button
            {
                Content = "❌",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            }
        }
            };

            var chip = new Border
            {
                Background = Brushes.LightBlue,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6),
                Margin = new Thickness(3),
                Child = chipPanel
            };

            if (chipPanel.Children[1] is Button removeButton)
            {
                removeButton.Click += (_, __) =>
                {
                    _filterSummaryPanel.Children.Remove(chip);
                    _filterGroups[0].Conditions.RemoveAll(c =>
                        c.Attribute == apiAttr && c.Operator == op && c.Value == rawValue);
                    _ = UpdatePreviewAsync();
                };
            }

            // Prepare the filter payload in the required format
            var filterPayload = new
            {
                Filter = new
                {
                    Attribute = apiAttr,
                    Operator = op,  // Use "like" for contains or other supported operators
                    Value = rawValue,
                    Type = "string"  // This is required to match the API payload structure
                },
                Sort = (object)null,  // No sort provided in the request
                Start = 1,
                PageSize = 50
            };

            // Log the final filter payload that will be sent to the API
            LogToUI($"🔍 API Payload:\n{JsonConvert.SerializeObject(filterPayload, Formatting.Indented)}\n");

            // Add the new filter condition
            _filterGroups[0].Conditions.Add(new FilterCondition
            {
                Attribute = apiAttr,
                Operator = op,
                Value = rawValue
            });

            _filterSummaryPanel.Children.Add(chip);
            _ = UpdatePreviewAsync();  // Trigger live preview with updated filter

        }





        private void OnAuthStatusChanged(string status)
        {
            Console.WriteLine($"[DEBUG] Auth status changed to: {status}");

            if (_authStatusText == null || _tokenTimerText == null)
            {
                Console.WriteLine("⚠️ AuthStatusText or TokenTimerText is null.");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (status == "Authenticated")
                {
                    _authStatusText.Text = "🟢 Authenticated";
                    _authStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);

                    _tokenTimerText.Foreground = new SolidColorBrush(Colors.LightGray);
                    _tokenTimerText.Text = "🕒 Token valid..."; // placeholder, will be updated by timer
                }
                else
                {
                    _authStatusText.Text = "🔴 Not Authenticated";
                    _authStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);

                    _tokenTimerText.Text = string.Empty;
                }

                _authStatusText.FontWeight = FontWeight.Bold;
            });
        }


        // Dictionary to track whether the instruction progress has already been fetched
        private readonly HashSet<int> _instructionsProgressFetched = new HashSet<int>();

        private async Task FetchInstructionProgressAsync(int instructionId)
        {
            // Check if progress for this instruction has already been fetched
            if (_instructionsProgressFetched.Contains(instructionId))
            {
                // If already fetched, skip and log less verbosity
                Console.WriteLine($"✅ Progress already fetched for Instruction ID {instructionId}. Skipping...");
                return;
            }

            try
            {
                // Indicate the start of fetching progress
                Console.WriteLine($"⏳ Fetching progress for Instruction ID {instructionId}...");

                // Simulate the progress fetching logic here (replace with actual API call)
                var progress = await FetchProgressFromAPI(instructionId);

                // Log the summary after successfully fetching progress
                Console.WriteLine($"📊 Instruction Progress Summary for ID {instructionId}: {progress}");

                // Mark this instruction as processed
                _instructionsProgressFetched.Add(instructionId);

            }
            catch (Exception ex)
            {
                // Catch and log any errors during fetching progress
                Console.WriteLine($"⚠️ Error fetching progress for Instruction ID {instructionId}: {ex.Message}");
            }
        }

        private async Task<JObject> FetchProgressFromAPI(int instructionId)
        {
            try
            {
                // Prepare HttpClient for the request
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                // Prepare the URL and payload
                string url = $"https://{_selectedPlatform?.Url}/consumer/InstructionStatistics/Combined/{instructionId}";

                // Log the API call using ApiLogger
                System.Net.HttpStatusCode? httpStatus = null;
                var responseContent = await ApiLogger.LogApiCallAsync(
                    label: "InstructionProgress",
                    endpoint: url,
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync(url);
                        httpStatus = response.StatusCode;
                        var resultJson = await response.Content.ReadAsStringAsync();
                        return resultJson;
                    },
                    payloadJson: "" // No request body for GET request
                );

                if (httpStatus == System.Net.HttpStatusCode.Unauthorized)
                {
                    ForceUnauthenticatedState("InstructionProgress returned 401");
                    return null;
                }

                // Check if the response was successful
                var response = JObject.Parse(responseContent);
                if (!response.HasValues)
                {
                    LogToUI($"❌ Failed to fetch progress for instruction {instructionId}: No response data.\n");
                    return null;
                }

                return response;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching progress for instruction {instructionId}: {ex.Message}\n");
                return null;
            }
        }






        public void UpdateTokenTimerText(string remaining)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_tokenTimerText != null)
                {
                    _tokenTimerText.Text = $"🕒 {remaining}";
                }
            });
        }



        // Event handler for the Logout button
        private async void LogoutButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_authService != null)
            {
                Console.WriteLine("🚪 Logout triggered.");
                await _authService.LogoutAsync();  // performs actual cleanup if needed

                _token = null;

                UpdateAuthStatusIndicator(false); // ✅ use this instead of manual label update

                LogToUI("❌ Logged out successfully.\n");

                // Optional UI cleanup
                _instructionListBox.ItemsSource = null;
                _resultsPanel.Children.Clear();
                _historyListBox.ItemsSource = null;

                // Update button states
                _loginButton.IsEnabled = true;
                _logoutButton.IsEnabled = false;
            }
            else
            {
                Console.WriteLine("❌ _authService is null during logout.");
            }
        }



        private bool _platformSelectionHandlerWired;

        private async Task InitializePlatformAsync()
        {
            LoadConfig();

            _platformConfigs = _configHelper.GetPlatformConfigs();
            if (_platformConfigs == null || !_platformConfigs.Any())
            {
                LogToUI("⚠️ No platforms found in config!\n");
                return;
            }            // Populate dropdown (display Alias but keep Url for API calls)
            var items = _platformConfigs
                .Where(p => !string.IsNullOrWhiteSpace(p?.Url))
                .Select(p => new DexInstructionRunner.Models.PlatformListItem(
                    string.IsNullOrWhiteSpace(p?.Alias) ? DexInstructionRunner.Services.LogRedaction.SafeAliasFromHost(p!.Url) : p!.Alias,
                    p!.Url))
                .ToList();

            _platformUrlDropdown.ItemsSource = items;
            // Wire selection changed ONCE (avoid multiple subscriptions if InitializePlatformAsync is called again)
            if (!_platformSelectionHandlerWired)
            {
                _platformSelectionHandlerWired = true;

                _platformUrlDropdown.SelectionChanged += async (_, __) =>
                {
                    try
                    {
                        if (_suppressPlatformSelectionChanged)
                            return;

                        if (_platformUrlDropdown.SelectedItem is DexInstructionRunner.Models.PlatformListItem item &&
                            !string.IsNullOrWhiteSpace(item.Url))
                            await HandlePlatformSelectedAsync(item.Url, isInitialLoad: false);
                    }
                    catch (Exception ex)
                    {
                        try { LogToUI($"❌ Platform SelectionChanged failed: {ex.Message}\n"); } catch { }
                    }
                };
            }

            // Pick default platform (by saved default alias if present, otherwise first entry)
            var preferredAlias = _configHelper?.GetDefaultPlatformAlias();
            var defaultPlatform = (!string.IsNullOrWhiteSpace(preferredAlias))
                ? _platformConfigs.FirstOrDefault(p => string.Equals(p?.Alias, preferredAlias, StringComparison.OrdinalIgnoreCase))
                : null;

            defaultPlatform ??= _platformConfigs.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p?.Url));
            if (defaultPlatform == null || string.IsNullOrWhiteSpace(defaultPlatform.Url))
            {
                LogToUI("⚠️ Platform config exists but contains no valid Url values.\n");
                return;
            }

            // Set selection (this may NOT fire SelectionChanged reliably, so we call handler explicitly)
            // Set selection WITHOUT triggering the SelectionChanged handler
            _suppressPlatformSelectionChanged = true;
            try
            {
                _platformUrlDropdown.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Url, defaultPlatform.Url, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
            }
            finally
            {
                _suppressPlatformSelectionChanged = false;
            }

            // Explicitly run the same code path as a dropdown selection (single call)
            await HandlePlatformSelectedAsync(defaultPlatform.Url, isInitialLoad: true);

        }

        private async Task HandlePlatformSelectedAsync(string selectedUrl, bool isInitialLoad)
        {
            // Update platform based on selected URL
            _selectedPlatform = _configHelper.GetSelectedPlatform(selectedUrl);
            _consumerName = _selectedPlatform?.Consumer ?? "Explorer";
            _defaultMG = _selectedPlatform?.DefaultMG ?? "Default";

            var displayPlatform = _selectedPlatform?.Alias;
            if (string.IsNullOrWhiteSpace(displayPlatform))
                displayPlatform = _selectedPlatform?.Url;

            LogToUI(isInitialLoad
                ? $"🌐 Default platform loaded: {displayPlatform}\n"
                : $"🌐 Switched platform to: {displayPlatform}\n");

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("⚠️ Selected platform is invalid.\n");
                return;
            }

            _fqdnManagerSelectedItems?.Clear();
            _lastInstructionFqdnSearchRaw = string.Empty;
            _instructionFqdnSearchStart = 1;
            _instructionFqdnSearchLastCount = 0;

            Dispatcher.UIThread.Post(() =>
            {
                _fqdnTextBox?.Clear();

                if (_selectedFqdnListBox != null)
                {
                    if (_fqdnManagerSelectedItems != null)
                        _fqdnManagerSelectedItems.Clear();

                    if (_selectedFqdnListBox != null)
                        _selectedFqdnListBox.SelectedItem = null;
                }

                if (_instructionFqdnSearchResultsListBox != null)
                {
                    _instructionFqdnSearchResultsListBox.ItemsSource = null;
                    _instructionFqdnSearchResultsListBox.SelectedItem = null;
                }

          /*      if (_instructionFqdnSearchResultsBorder != null)
                    _instructionFqdnSearchResultsBorder.IsVisible = false;
          */

                UpdateFqdnManagerSelectedHeader();

                if (ManagementGroupComboBox != null)
                {
                    ManagementGroupComboBox.ItemsSource = null;
                    try { ManagementGroupComboBox.SelectedItem = null; } catch { }
                }
            });

            // Reset UI/state for a real platform swap (keep this, but don't destroy tokens)
            UpdateAuthStatusIndicator(false);
            ResetApplicationState();

            // Clear UI safely
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _fqdnTextBox?.Clear();
                if (_selectedFqdnListBox != null)
                    _selectedFqdnListBox.ItemsSource = new List<string>();

                ParametersPanel?.Children.Clear();

                if (InstructionDescription != null)
                    InstructionDescription.Text = "";

                if (TokenTimerText != null)
                    TokenTimerText.IsVisible = false;

                _historyListBox?.ClearValue(ItemsControl.ItemsSourceProperty);

                if (ResultsPanel != null)
                    ResultsPanel.Children.Clear();

                if (ManagementGroupComboBox != null)
                {
                    ManagementGroupComboBox.ItemsSource = null;
                    try { ManagementGroupComboBox.SelectedItem = null; } catch { }
                }
            });

            string platformUrl = _selectedPlatform.Url;


            // 1) Try in-memory session token first (fast, no prompt)
            if (_sessionTokenByPlatformUrl != null &&
                _sessionTokenByPlatformUrl.TryGetValue(platformUrl, out var sessionToken) &&
                !string.IsNullOrWhiteSpace(sessionToken))
            {
                _authService.Token = sessionToken;
                _token = sessionToken;
                LogToUI($"✅ Reusing session token for {displayPlatform}");
            }
            else
            {
                // 2) Try saved token (disk/secure storage depending on your auth service)
                if (_authService.TryGetSavedToken(platformUrl, out var savedToken) && !string.IsNullOrWhiteSpace(savedToken))
                {
                    _authService.Token = savedToken;
                    _token = savedToken;
                    LogToUI($"✅ Loaded cached token for {displayPlatform}");

                    // Store into session cache too so subsequent swaps don't touch disk
                    _sessionTokenByPlatformUrl[platformUrl] = savedToken;
                }
                else
                {
                    LogToUI($"ℹ️ No cached token found for {displayPlatform}");

                    // 3) Authenticate (may prompt)
                    try
                    {
                        var result = await _authService.AuthenticateAsync(_selectedPlatform);
                        _authService.Token = result?.Token;
                        _token = _authService.Token;

                        if (!string.IsNullOrWhiteSpace(_token))
                        {
                            _sessionTokenByPlatformUrl[platformUrl] = _token;
                            LogToUI($"✅ Authenticated and cached session token for {platformUrl}\n");
                        }
                        else
                        {
                            LogToUI($"❌ Authentication returned an empty token for {platformUrl}\n");
                            UpdateAuthStatusIndicator(false);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Authentication failed: {ex.Message}\n");
                        UpdateAuthStatusIndicator(false);
                        return;
                    }
                }
            }

            // Validate expiration; refresh if expired
            try
            {
                var expiration = string.IsNullOrWhiteSpace(_token) ? (DateTime?)null : DumpJwtPayload(_token);
                if (expiration != null)
                {
                    _authService.SetExpirationTime(expiration.Value);
                    var timeRemaining = expiration.Value - DateTime.UtcNow;
                    _authService.NotifyTokenTimeRemaining(timeRemaining);

                    if (timeRemaining <= TimeSpan.Zero)
                    {
                        LogToUI("❌ Token expired. Re-authenticating...\n");

                        var result = await _authService.AuthenticateAsync(_selectedPlatform);
                        _authService.Token = result?.Token;
                        _token = _authService.Token;

                        if (string.IsNullOrWhiteSpace(_token))
                        {
                            LogToUI($"❌ Token refresh failed for {platformUrl}\n");
                            UpdateAuthStatusIndicator(false);
                            return;
                        }

                        _sessionTokenByPlatformUrl[platformUrl] = _token;

                        var newExp = DumpJwtPayload(_token);
                        if (newExp != null)
                        {
                            _authService.SetExpirationTime(newExp.Value);
                            var newTimeRemaining = newExp.Value - DateTime.UtcNow;
                            _authService.NotifyTokenTimeRemaining(newTimeRemaining);
                        }

                        LogToUI($"✅ Token refreshed for platform: {platformUrl}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Token validation failed: {ex.Message}\n");
                // keep going; token might still be usable depending on your environment
            }

            UpdateAuthStatusIndicator(!string.IsNullOrWhiteSpace(_token));
            if (string.IsNullOrWhiteSpace(_token))
                return;

            _authExpiredDialogShown = false;

            // Load user/platform data - do not let failures break token caching/state
            try { await GetPrincipalNameAsync(platformUrl, _token); }
            catch (Exception ex) { LogToUI($"⚠️ Principal lookup failed: {ex.Message}\n"); }

            try { await LoadAllDataAsync(); }
            catch (Exception ex) { LogToUI($"⚠️ LoadAllDataAsync failed: {ex.Message}\n"); }

            // Only check legacy mode AFTER we have a token
            try
            {
                _isLegacyVersion = await VersionService.IsLegacyVersionAsync(platformUrl, _token);
                LogToUI($"🧭 Legacy mode = {_isLegacyVersion}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Legacy mode check failed: {ex.Message}\n");
            }
        }







        public void UpdateTokenTimerVisibility(bool isVisible)
        {
            if (TokenTimerText != null)
                TokenTimerText.IsVisible = isVisible;
        }

        private async Task TryLoadSavedTokenAsync()
        {
            if (_selectedPlatform == null)
                return;

            if (_authService.TryGetSavedToken(_selectedPlatform.Url, out var savedToken))
            {
                _token = savedToken;
                _authService.Token = savedToken;

                var expiration = _authService.GetTokenExpirationTime();
                if (expiration != null)
                {
                    _authService.SetExpirationTime(expiration.Value);
                    var timeRemaining = expiration.Value - DateTime.UtcNow;
                    _authService.NotifyTokenTimeRemaining(timeRemaining);
                    UpdateTokenTimerVisibility(false);
                }

                UpdateAuthStatusIndicator(true);
                LogToUI($"✅ Loaded saved token for {_selectedPlatform.Url}.");
            }
            else
            {
                LogToUI($"🔒 No saved token found for {_selectedPlatform.Url}.");
            }
        }


        private void SwitchToResultsTab()
        {
            var tabControl = this.FindControl<TabControl>("MainTabControl");
            if (tabControl != null)
            {
                tabControl.SelectedIndex = 3; // Assuming Results is the 4th tab
            }
        }
        private void SetTtlBoxToAccessibleDefault(TextBox box)
        {
            var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;

            box.Background = isDark ? Brushes.Black : Brushes.White;
            box.Foreground = isDark ? Brushes.White : Brushes.Black;
            box.BorderBrush = Brushes.Gray;
            box.CaretBrush = isDark ? Brushes.White : Brushes.Black;
            box.SelectionBrush = isDark ? Brushes.White : Brushes.Black;

            // Patch-once event to reapply foreground after typing (avoids silent override)
            box.TextChanged -= TtlBox_TextChanged;
            box.TextChanged += TtlBox_TextChanged;

            box.InvalidateVisual();
            box.InvalidateMeasure();
        }


        private void TtlBox_TextChanged(object? sender, EventArgs e)
        {
            if (sender is TextBox box)
            {
                var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                box.Foreground = isDark ? Brushes.White : Brushes.Black;
                box.CaretBrush = isDark ? Brushes.White : Brushes.Black;
            }
        }



        private void ForceResetTtlBox(TextBox box)
        {
            box.ClearValue(TextBox.BackgroundProperty);
            box.ClearValue(TextBox.ForegroundProperty);
            box.ClearValue(TextBox.BorderBrushProperty);

            box.Background = TryGetBrush("ThemeBackgroundBrush", "SystemControlBackgroundBaseHighBrush", Brushes.White);
            box.Foreground = TryGetBrush("ThemeForegroundBrush", "SystemControlForegroundBaseHighBrush", Brushes.Black);
            box.BorderBrush = TryGetBrush("SystemControlForegroundBaseLowBrush", fallback: Brushes.Gray);

            box.InvalidateVisual();
            box.InvalidateMeasure();
        }

        private void ResetBoxToTheme(TextBox box)
        {
            box.ClearValue(TextBox.BackgroundProperty);
            box.ClearValue(TextBox.ForegroundProperty);
            box.ClearValue(TextBox.BorderBrushProperty);

            box.Background = TryGetBrush("ThemeBackgroundBrush", "SystemControlBackgroundBaseHighBrush", Brushes.White);
            box.Foreground = TryGetBrush("ThemeForegroundBrush", "SystemControlForegroundBaseHighBrush", Brushes.Black);
            box.BorderBrush = TryGetBrush("SystemControlForegroundBaseLowBrush", fallback: Brushes.Gray);

            box.InvalidateVisual();
            box.InvalidateMeasure();
        }

        private void ValidateTtl(TextBox box, ComboBox unitBox)
        {
            if (box == null || unitBox == null)
                return;

            string unit = (unitBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "minutes";
            int min = 10, max = 10080;

            switch (unit)
            {
                case "hours": min = 1; max = 168; break;
                case "days": min = 1; max = 7; break;
            }

            bool isValid = int.TryParse(box.Text, out int value) && value >= min && value <= max;

            if (isValid)
            {
                SetTtlBoxToAccessibleDefault(box);
                _instructionTtlBox.Classes.Clear();

            }
            else
            {
                box.Background = Brushes.DarkRed;
                box.BorderBrush = Brushes.Yellow;
                box.Foreground = Brushes.White;
                Console.WriteLine($"[DEBUG] TTL value {box.Text} invalid for unit '{unit}'. Expected range {min}–{max}");
            }
        }

        /// <summary>
        /// Attempts to retrieve a brush from one or two theme resource names, falling back to a default if none are found.
        /// </summary>
        private IBrush TryGetBrush(string primary, string secondary = null, IBrush fallback = null)
        {
            var brush = Application.Current.FindResource(primary) as IBrush;

            if (brush == null && !string.IsNullOrEmpty(secondary))
                brush = Application.Current.FindResource(secondary) as IBrush;

            if (brush == null)
                brush = fallback;

            Console.WriteLine($"[THEME] Brush [{primary}] fallback [{secondary}] = {brush}");

            return brush;
        }


        private void InitializeFilterControls()
        {
            var attributeCombo = this.FindControl<ComboBox>("FilterAttributeComboBox");
            var operatorCombo = this.FindControl<ComboBox>("FilterOperatorComboBox");
            var valueCombo = this.FindControl<ComboBox>("FilterValueComboBox");

            // Static filterable attributes
            var attributes = new List<string> { "OsType", "DeviceType", "CoverageTags" };
            attributeCombo.ItemsSource = attributes;

            attributeCombo.SelectionChanged += (_, __) =>
            {
                var selectedAttr = attributeCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(selectedAttr)) return;

                // Operator dropdown based on type
                List<string> operators = selectedAttr switch
                {
                    "OsType" or "DeviceType" or "CoverageTags" => new List<string> { "is", "is not", "contains" },
                    _ => new List<string>()
                };

                operatorCombo.ItemsSource = operators;
                operatorCombo.SelectedIndex = 0;

                // Example static values for now — replace with real data later
                if (selectedAttr == "OsType")
                {
                    valueCombo.ItemsSource = new List<string> { "Windows", "Linux", "macOS" };
                }
                else if (selectedAttr == "DeviceType")
                {
                    valueCombo.ItemsSource = new List<string> { "Laptop", "Desktop", "Server", "Tablet" };
                }
                else if (selectedAttr == "CoverageTags")
                {
                    valueCombo.ItemsSource = new List<string> { "Region:US", "BU:Finance", "AutoPilot:Yes" };
                }

                valueCombo.SelectedIndex = 0;
            };
        }

        private void OnTargetingModeChanged(object? sender, RoutedEventArgs e)
        {
            try
            {
                var selectedMode = GetSelectedTargetingMode();
                UpdateTargetingModePanels(selectedMode);
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Failed to update targeting mode UI: {ex.Message}\n");
            }
        }



        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public class ManagementGroup
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int UsableId { get; set; }

            public override string ToString() => Name;
        }


        private void ShowLogToggle_Checked(object? sender, RoutedEventArgs e)
        {
            this.FindControl<TextBox>("LogTextBox").IsVisible = true;
        }

        private void ShowLogToggle_Unchecked(object? sender, RoutedEventArgs e)
        {
            this.FindControl<TextBox>("LogTextBox").IsVisible = false;
        }
        public void ClearLogButton_Click(object? sender, RoutedEventArgs e)
        {
            _logTextBox.Text = string.Empty;
        }



        private void ValidateTtlInputs()
        {
            Console.WriteLine("[DEBUG] Running ValidateTtlInputs");
            ValidateTtl(_instructionTtlBox, this.FindControl<ComboBox>("InstructionTtlUnitDropdown"));
            ValidateTtl(_responseTtlBox, this.FindControl<ComboBox>("ResponseTtlUnitDropdown"));
        }




        private void LoadConfig()
        {
            string jsonFile = "appsettings.json";
            string currentDir = Directory.GetCurrentDirectory();
            string exeDir = AppContext.BaseDirectory;

            string[] possiblePaths =
            {
        System.IO.Path.Combine(currentDir, jsonFile),
        System.IO.Path.Combine(exeDir, jsonFile)
    };

            IConfigurationRoot config = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    LogToUI($"✅ Found config at: {path}\n");
                    config = (IConfigurationRoot)new ConfigurationBuilder()
                        .SetBasePath(System.IO.Path.GetDirectoryName(path)!)
                        .AddJsonFile(jsonFile, optional: false)
                        .Build();
                    break;
                }
            }

            if (config == null)
            {
                LogToUI("❌ Could not find appsettings.json in expected locations.\n");
                return;
            }

            _config = config;
            // IMPORTANT:
            // Platform dropdown population + switching logic is handled by InitializePlatformAsync/HandlePlatformSelectedAsync.
            // Do NOT wire SelectionChanged here; this method is called in multiple startup paths and duplicate handlers
            // (and partial UI initialization) can lead to invalid/null selections and broken platform switching.
        }


        public bool SetDefaultManagementGroup(string platformUrl, string mgName)
        {
            try
            {
                if (_managementGroupComboBox.SelectedItem is string selectedMg)
                {
                    _configHelper.UpdateDefaultManagementGroup(_selectedPlatform.Url, selectedMg);
                    LogToUI($"✅ Set '{selectedMg}' as default Management Group for {_selectedPlatform.Url}.\n");
                }

                // Use the same resolved path for both reads and writes.
                string originalPath = _configHelper?.ConfigFullPath ?? System.IO.Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                if (!File.Exists(originalPath))
                {
                    // Create a minimal config file so the user can start from an empty list.
                    try { _configHelper?.SavePlatformConfigs(_configHelper?.GetPlatformConfigs() ?? new List<PlatformConfig>()); } catch { }
                }
                if (!File.Exists(originalPath))
                {
                    LogToUI("❌ Could not create writable appsettings.json to update.\n");
                    return false;
                }

                return _configHelper?.UpdateDefaultManagementGroup(platformUrl, mgName) == true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to update DefaultMG: {ex.Message}");
                return false;
            }
        }



        private void OnClearFqdnsClicked(object? sender, RoutedEventArgs e)
        {
            var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
            if (listBox == null) return;

            listBox.ItemsSource = new List<string>();
            // UpdateFqdnListCountLabels();
            LogToUI("🧹 Cleared FQDN list.");
        }

        private void OnRemoveSelectedFqdnsClicked(object? sender, RoutedEventArgs e)
        {
            var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
            var countLabel = this.FindControl<TextBlock>("FqdnCountLabel"); // Updated label name

            if (listBox == null || listBox.Items == null || listBox.SelectedItems == null)
            {
                Console.WriteLine("❌ FQDN list box or selection is null.");
                return;
            }

            var selected = listBox.SelectedItems.Cast<object>()
                .OfType<string>()
                .ToList();

            if (selected.Count == 0)
            {
                Console.WriteLine("ℹ️ No FQDNs selected.");
                return;
            }

            var remaining = listBox.Items.Cast<object>()
                .OfType<string>()
                .Except(selected)
                .ToList();

            listBox.ItemsSource = remaining;

            if (countLabel != null)
                countLabel.Text = $"Selected FQDNs ({remaining.Count})";
        }


        public static void RenderChartResults(JArray chartData, string chartType, StackPanel resultsPanel, string xField, string yField, bool isDark)
        {
            resultsPanel.Children.Clear();

            switch (chartType?.ToLowerInvariant())
            {
                case "pie":
                    PieChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
                case "bar":
                case "smartbar":
                case "column":
                    BarChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
                case "line":
                    LineChartRenderer.Render(chartData, resultsPanel, xField, yField);
                    break;
                case "stackedarea":
                    StackedAreaChartRenderer.Render(chartData, resultsPanel, xField, yField);
                    break;
                default:
                    BarChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
            }
        }


        private void UpdateInstructionList(string? query)
        {
            var keywords = query?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _instructionMap.Keys
                : _instructionMap.Keys.Where(name => keywords.All(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)));

            _instructionListBox.ItemsSource = filtered.Select(name => name.Trim()).ToList();
        }

        private void UpdateFqdnInstructionList(string? query)
        {
            if (_fqdnInstructionListBox == null)
                return;

            var keywords = query?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _instructionMap.Keys
                : _instructionMap.Keys.Where(name => keywords.All(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)));

            _fqdnInstructionListBox.ItemsSource = filtered.Select(name => name.Trim()).ToList();
        }

        private async void FqdnInstructionListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_fqdnInstructionListBox?.SelectedItem is not string selectedName)
                    return;

                if (!_instructionMap.TryGetValue(selectedName, out int instructionId))
                    return;

                var def = _instructionDefinitions.FirstOrDefault(i => i.Id == instructionId);
                if (def == null)
                {
                    // Fallback so the tab doesn't break if definitions aren't in memory yet.
                    def = new InstructionDefinition
                    {
                        Id = instructionId,
                        Name = selectedName,
                        Description = "",
                        Parameters = new List<Parameter>()
                    };
                }

                _selectedInstruction = def;
                _lastInstructionDefinition = def;

                _currentInstructionSchemaDefinitionId = -1;
                _currentInstructionSchemaColumns.Clear();

                // Switching instructions should reset any run filters and close the flyout.
                CloseAttachedFlyout(_fqdnRunFilterButton);
                ClearFqdnRunFilter();

                // Switching instructions should reset any run filters and close the flyout.
                CloseAttachedFlyout(_instrRunFilterButton);
                ClearInstructionRunFilter();

                // If the user has "Filtered" selected for Run scope on the FQDN tab, immediately fetch schema
                // and populate the Run Filter UI.
                if (_fqdnRunScopeFilteredRadio?.IsChecked == true)
                {
                    try
                    {
                        await EnsureSelectedInstructionSchemaLoadedAsync(forFqdnTab: true);
                        ApplySchemaColumnsToRunFilterUi();
                    }
                    catch { }
                }

                UpdateViewToggles(def);
                ApplyInstructionToFqdnTab(def, selectInList: false);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ FQDN instruction selection failed: {ex.Message}");
            }
        }

        private void SyncInstructionToFqdnTab()
        {
            if (_lastInstructionDefinition == null)
                return;

            ApplyInstructionToFqdnTab(_lastInstructionDefinition, selectInList: true);
        }

        private void ApplyInstructionToFqdnTab(InstructionDefinition definition, bool selectInList)
        {
            if (definition == null)
                return;

            var fqdnLabel = this.FindControl<TextBlock>("FqdnInstructionNameLabel");
            if (fqdnLabel != null)
                fqdnLabel.Text = $"Selected Instruction: {definition.Name}";

            if (_fqdnInstructionDescriptionText != null)
                _fqdnInstructionDescriptionText.Text = definition.Description ?? "";

            if (selectInList && _fqdnInstructionListBox != null)
                _fqdnInstructionListBox.SelectedItem = definition.Name;

            var fqdnParamPanel = TryFindControl<StackPanel>("FqdnParametersPanel");
            if (fqdnParamPanel != null)
                RenderParameters(fqdnParamPanel, definition.Parameters);
        }



        private bool AreTtlInputsValid()
        {
            return _instructionTtlBox.Background != Brushes.IndianRed &&
                   _responseTtlBox.Background != Brushes.IndianRed;
        }

        private bool IsTtlValid(TextBox box, ComboBox unitBox)
        {
            if (box == null || unitBox == null)
                return false;

            string unit = (unitBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "minutes";
            int min = 10, max = 10080;

            switch (unit)
            {
                case "hours": min = 1; max = 168; break;
                case "days": min = 1; max = 7; break;
            }

            return int.TryParse(box.Text, out int value) && value >= min && value <= max;
        }


        private async void OnRunClicked(object? sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                LogToUI("⚠️ Instruction is already running. Please wait...\n");
                return;
            }

            _isRunning = true;

            // If the run filter flyout is open, close it once the run is kicked off.
            CloseAttachedFlyout(_instrRunFilterButton);

            try
            {
                if (_instructionListBox.SelectedItem is not string selectedName || !_instructionMap.TryGetValue(selectedName, out int instructionId))
                {
                    LogToUI("❌ No instruction selected.\n");
                    return;
                }

                var paramPanel = TryFindControl<StackPanel>("ParametersPanel");
                var fqdnParamPanel = TryFindControl<StackPanel>("FqdnParametersPanel");

                if (paramPanel == null || fqdnParamPanel == null)
                {
                    LogToUI("ℹ️ Parameter panels not found (continuing with no parameters).\n");
                    // no return

                }

                var parametersDict = new Dictionary<string, string>();
                var allParamRows = Enumerable.Empty<StackPanel>();

                if (paramPanel != null)
                    allParamRows = allParamRows.Concat(paramPanel.Children.OfType<StackPanel>());

                if (fqdnParamPanel != null)
                    allParamRows = allParamRows.Concat(fqdnParamPanel.Children.OfType<StackPanel>());
                foreach (var row in allParamRows)
                {
                    if (row.Children.Count >= 2 &&
                        row.Children[1] is Border border &&
                        border.Child is Control input &&
                        input.Tag is string paramName)
                    {
                        string value = input switch
                        {
                            ComboBox combo => combo.SelectedItem?.ToString() ?? "",
                            TextBox textBox => textBox.Text,
                            _ => ""
                        };

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parametersDict[paramName] = value;

                            if (input is TextBox textBox)
                                textBox.Background = Brushes.Transparent;
                            else if (input is ComboBox comboBox)
                                comboBox.Background = Brushes.Transparent;
                        }
                        else
                        {
                            LogToUI($"⚠️ Parameter '{paramName}' is empty and may be required.\n");

                            if (input is TextBox textBox)
                                textBox.Background = Brushes.IndianRed;
                            else if (input is ComboBox comboBox)
                                comboBox.Background = Brushes.IndianRed;
                        }


                    }
                }

                int.TryParse(_instructionTtlBox?.Text, out var instructionTtl);
                int.TryParse(_responseTtlBox?.Text, out var responseTtl);
                if (instructionTtl <= 0) instructionTtl = 60;
                if (responseTtl <= 0) responseTtl = 120;

                // Track TTL countdown for Results tab
                _currentInstructionRunStartedUtc = DateTime.UtcNow;
                _currentInstructionTtlMinutes = instructionTtl;
                UpdateResultsTtlCountdownUi();

                var payload = new Dictionary<string, object>
                {
                    ["DefinitionId"] = instructionId,
                    ["KeepRaw"] = 1,
                    ["Parameters"] = parametersDict.Select(p => new Dictionary<string, string>
                    {
                        ["Name"] = p.Key,
                        ["Value"] = p.Value
                    }).ToList(),
                    ["InstructionTtlMinutes"] = instructionTtl,
                    ["ResponseTtlMinutes"] = responseTtl,
                    ["ReadablePayload"] = selectedName,
                    ["Devices"] = new List<string>(),
                    ["Scope"] = new { }
                };


                // Apply server-side ResultsFilter when Run scope is set to Filtered
                if (_runScopeFilteredRadio?.IsChecked == true)
                {
                    var rf = BuildRunResultsFilterObject(isFqdnTab: false);
                    payload["ResultsFilter"] = rf; // can be null (API will ignore)
                }
                else
                {
                    payload["ResultsFilter"] = null;
                }

                string endpoint;
                string? covTextForRun = null;
                string selectedMode = GetSelectedTargetingMode();

                var fqdnListBox = this.FindControl<ListBox>("SelectedFqdnListBox");
                if (fqdnListBox?.IsVisible == true && fqdnListBox.Items?.Cast<string>().Any() == true)
                {
                    var distinct = fqdnListBox.Items.Cast<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (distinct.Count > 10)
                    {
                        await ShowSimpleDialogAsync("FQDN List", "Maximum of 10 FQDNs allowed");
                        return;
                    }

                    var fqdns = distinct;
                    payload["Devices"] = fqdns;
                    payload["Scope"] = new { };
                    endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions/Targeted";
                    covTextForRun = fqdns.Count > 0
                        ? $"FQDN == {string.Join(", ", fqdns)}"
                        : null;
                }
                else if (selectedMode == "FQDN")
                {
                    string rawFqdnText;

                    if (_fqdnManagerSelectedItems != null && _fqdnManagerSelectedItems.Count > 0)
                    {
                        rawFqdnText = string.Join(",",
                            _fqdnManagerSelectedItems
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Take(10));
                    }
                    else
                    {
                        rawFqdnText = _fqdnTextBox?.Text ?? string.Empty;
                    }

                    var fqdnList = rawFqdnText
                        .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => (x ?? string.Empty).Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToList();

                    if (fqdnList.Count == 0)
                    {
                        LogToUI("❌ FQDN input required.\n");
                        return;
                    }

                    if (fqdnList.Count > 10)
                    {
                        await ShowSimpleDialogAsync("FQDN List", "Maximum of 10 FQDNs allowed");
                        LogToUI($"❌ Too many FQDNs provided ({fqdnList.Count}). Max is 10.\n");
                        return;
                    }

                    if (fqdnList.Count > 1)
                    {
                        payload["Devices"] = fqdnList;
                        payload["Scope"] = new { };
                        endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions/Targeted";
                        covTextForRun = $"FQDN == {string.Join(", ", fqdnList)}";
                    }
                    else
                    {
                        var fqdn = fqdnList[0];

                        payload["Scope"] = new Dictionary<string, string>
                        {
                            ["Attribute"] = "FQDN",
                            ["Operator"] = "==",
                            ["Value"] = fqdn
                        };

                        endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions";
                        covTextForRun = $"FQDN == {fqdn}";
                    }
                }
                else if (selectedMode == "Management Group")
                {
                    var groups = ResolveSelectedManagementGroups();

                    if (groups == null || groups.Count == 0)
                    {
                        LogToUI("❌ Select a valid management group.\n");
                        return;
                    }

                    // Single MG: keep original behavior exactly
                    if (groups.Count == 1)
                    {
                        var mg = groups[0];

                        // "All devices" can be represented as 0 or -1 depending on platform/list population.
                        if (mg.UsableId <= 0)
                        {
                            // All devices
                            if (_authService.IsMultiTenant)
                            {
                                payload["Scope"] = new Dictionary<string, object>
                                {
                                    ["Attribute"] = "managementgroup",
                                    ["Operator"] = "==",
                                    ["Value"] = "global"
                                };
                            }
                            else
                            {
                                payload["Scope"] = new { };
                            }
                        }
                        else
                        {
                            payload["Scope"] = new Dictionary<string, object>
                            {
                                ["Attribute"] = "managementgroup",
                                ["Operator"] = "==",
                                ["Value"] = mg.UsableId
                            };
                        }
                    }
                    else
                    {
                        // Multiple MGs: use the AND/OR toggle and the dynamic-style payload shape
                        var andRadio = TryFindControl<Avalonia.Controls.RadioButton>("MgmtGroupAndRadioButton");
                     //   bool useAnd = andRadio != null && andRadio.IsChecked == true; // missing/hidden => false => OR
                        bool useAnd = false; // temporarily force OR
                        var operands = new List<object>();

                        foreach (var mg in groups.Where(g => g != null && g.UsableId > 0))
                        {
                            operands.Add(new Dictionary<string, object>
                            {
                                ["Attribute"] = "managementgroup",
                                ["Operator"] = "=",
                                ["Value"] = mg.UsableId.ToString()
                            });
                        }

                        if (operands.Count == 0)
                        {
                            // fallback to "all devices" semantics if everything selected is non-usable
                            if (_authService.IsMultiTenant)
                            {
                                payload["Scope"] = new Dictionary<string, object>
                                {
                                    ["Attribute"] = "managementgroup",
                                    ["Operator"] = "==",
                                    ["Value"] = "global"
                                };
                            }
                            else
                            {
                                payload["Scope"] = new { };
                            }
                        }
                        else
                        {
                            payload["Scope"] = new Dictionary<string, object>
                            {
                                ["Operator"] = useAnd ? "AND" : "OR",
                                ["Operands"] = operands
                            };
                        }
                    }

                    endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions";
                }
                else if (selectedMode == "Coverage Tag")
                {
                    string tag = _coverageTagNameComboBox?.SelectedItem?.ToString() ?? "";
                    string value = _coverageTagValueComboBox?.SelectedItem?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(value))
                    {
                        LogToUI("❌ Coverage tag and value must be selected.\n");
                        return;
                    }

                    string tagExpression = $"{tag}={value}";
                    payload["Scope"] = new Dictionary<string, object>
                    {
                        ["Attribute"] = "TagTxt",
                        ["Operator"] = "",
                        ["Value"] = tagExpression
                    };

                    endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions";
                }
                else if (selectedMode == "Dynamic")
                {
                    payload["Scope"] = BuildDynamicScopeExpression();
                    endpoint = $"https://{_selectedPlatform?.Url}/consumer/Instructions";
                }
                else
                {
                    LogToUI("❌ No targeting method selected.\n");
                    return;
                }

                LogToUI($"🛰 Sending payload:\n{JsonConvert.SerializeObject(payload, Formatting.Indented)}\n");
                LogToUI($"📡 POST to: {endpoint}\n");
                LogToUI("📤 Parameters: " + JsonConvert.SerializeObject(payload["Parameters"]) + "\n");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content);
                var result = await response.Content.ReadAsStringAsync();
                LogToUI($"📬 Raw response: {result}\n");

                int responseId;
                try
                {
                    var responseObj = JsonConvert.DeserializeObject<dynamic>(result);
                    responseId = responseObj?.Id ?? instructionId;
                }
                catch
                {
                    responseId = instructionId;
                }

                _lastInstructionId = responseId;
                // Preserve the run coverage label using the response id key (prevents later header refresh from reverting to MG/All devices).
                try
                {
                    if (!string.IsNullOrWhiteSpace(covTextForRun))
                    {
                        _resultsCoverageOverride = covTextForRun;
                        _resultsCoverageOverrideInstructionId = responseId;
                        try
                        {
                            var covTb = this.FindControl<TextBlock>("ResultsContextCoverage");
                            if (covTb != null)
                                covTb.Text = covTextForRun;
                        }
                        catch { }
                    }
                }
                catch { }


                // Canonical active instruction id for Results live polling.
                _activeResultsInstructionId = _lastInstructionId;
                _activeResultsExpiresUtc = null;
                _lastProgressSentCount = -1;
                _lastProgressReceivedCount = -1;
                _lastProgressOutstandingCount = -1;
                _lastPeriodicResultsRefreshUtc = DateTime.MinValue;
                _lastAutoResultsRefreshUtc = DateTime.MinValue;

                // Ensure Results tab status + summary begin updating immediately
                _currentInstructionId = _lastInstructionId.ToString();
                _resultsStatusPollRunId++;
                var runId = _resultsStatusPollRunId;
                _ = RefreshInstructionStatusAsync(_currentInstructionId, runId);
                _ = ShowInstructionProgressAsync(_lastInstructionId, log: false);

                // Reset paging and load first page in the Results tab
                ResetResultsPagingState();
                _resultsPageSize = GetResultsPageSize();
                await LoadInstructionResultsPageAsync(_lastInstructionId, startRange: "0;0", resetPanels: true);

                _lastInstructionDefinition = _instructionDefinitions.FirstOrDefault(i => i.Id == instructionId);
                LogToUI($"📌 Last Instruction ID: {_lastInstructionId}\n");

                if (_lastInstructionDefinition != null)
                {
                    UpdateViewToggles(_lastInstructionDefinition);
                    try
                    {
                        ResultsContextInstructionName ??= this.FindControl<TextBlock>("ResultsContextInstructionName");
                        if (ResultsContextInstructionName != null)
                            ResultsContextInstructionName.Text = _lastInstructionDefinition.Name ?? string.Empty;
                    }
                    catch { }
                    LogToUI($"✅ Matched definition: {_lastInstructionDefinition.Name}\n");
                }

                LogToUI($"✅ Instruction sent.\n");

                await LoadInstructionHistoryAsync();
                UpdateView("Raw");
                await RefreshPendingApprovalsAsync();


                var tabControl = this.FindControl<TabControl>("MainTabControl");
                var resultsTab = tabControl?.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header?.ToString() == "Results");
                if (resultsTab != null)
                    tabControl.SelectedItem = resultsTab;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to send instruction: {ex.Message}\n");
            }
            finally
            {
                _isRunning = false;
            }
        }
        private async Task RefreshPendingApprovalsAsync()
        {
            try
            {
                if (_selectedPlatform != null && !string.IsNullOrWhiteSpace(_token))
                {
                    string url = $"https://{_selectedPlatform.Url}/Consumer/Approvals/notifications";

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                    client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(json);
                    var instructions = parsed["Instructions"] as JArray;

                    if (instructions != null && instructions.Any())
                    {
                        var list = new List<ApprovalInstruction>();

                        foreach (var inst in instructions)
                        {
                            var id = inst.Value<int?>("Id") ?? 0;
                            var name = inst.Value<string>("Name") ?? "";
                            var createdBy = inst.Value<string>("CreatedBy") ?? "";
                            var createdUtc = inst.Value<DateTime?>("CreatedTimestampUtc")?.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt") ?? "";


                            var (online, offline, targeted, totalOnline, percent) = await FetchTargetingImpactAsync(id);

                            list.Add(new ApprovalInstruction
                            {
                                Id = id,
                                InstructionName = name,
                                CreatedTimestampUtc = createdUtc,
                                TargetingPercent = percent
                            });
                        }

                        _pendingApprovals = list;
                        NotificationList.ItemsSource = _pendingApprovals;
                        LogToUI($"🔔 Refreshed {_pendingApprovals.Count} pending approval(s).");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI("❌ Failed to refresh approvals: " + ex.Message);
            }
        }


        private object BuildDynamicScopeExpression()
        {
            var allDefs = DynamicTargetingHelper.GetAttributes();
            var groupExprs = _filterGroups
                .Where(g => g.Filters.Count > 0)
                .Select(group => new Dictionary<string, object>
                {
                    ["Operator"] = group.Logic,
                    ["Operands"] = group.Filters
                        .Where(f => !string.IsNullOrWhiteSpace(f.Attribute) &&
                                    !string.IsNullOrWhiteSpace(f.Operator) &&
                                    !string.IsNullOrWhiteSpace(f.Value))
                        .Select(f =>
                        {
                            var attrDef = allDefs.FirstOrDefault(def => def.Name == f.Attribute);
                            if (attrDef == null)
                            {
                                LogToUI($"⚠️ Unknown attribute '{f.Attribute}'\n");
                                return null;
                            }

                            string apiAttr = attrDef.ApiName ?? f.Attribute;
                            string value = f.Value.Contains('|') ? f.Value.Split('|')[1].Trim() : f.Value;

                            if (string.IsNullOrWhiteSpace(value))
                            {
                                LogToUI($"⚠️ Skipping filter with empty value for attribute '{f.Attribute}'\n");
                                return null;
                            }

                            return new Dictionary<string, object>
                            {
                                ["Attribute"] = apiAttr,
                                ["Operator"] = f.Operator,
                                ["Value"] = value
                            };
                        })
                        .Where(op => op != null)
                        .ToList<object>()
                })
                .Where(group => ((List<object>)group["Operands"]).Count > 0)
                .ToList();

            // Handle Management Group selection (if selected)
            var mgComboBox = this.FindControl<ComboBox>("FilterManagementGroupComboBox");
            var selectedMG = mgComboBox?.SelectedItem as ManagementGroup;

            if (selectedMG != null)
            {
                LogToUI($"🔍 Adding Management Group filter: {selectedMG.UsableId}\n");

                // "All devices" can be represented as 0 or -1 depending on platform/list population.
                // Multi-tenant expects managementgroup == "global"; single-tenant uses empty scope (no MG filter).
                if (selectedMG.UsableId <= 0)
                {
                    if (_authService.IsMultiTenant)
                    {
                        groupExprs.Add(new Dictionary<string, object>
                        {
                            ["Operator"] = "==",
                            ["Operands"] = new List<object>
                            {
                                new { Attribute = "managementgroup", Operator = "==", Value = "global" }
                            }
                        });
                    }
                    // else: leave it out entirely (equivalent to {})
                }
                else
                {
                    groupExprs.Add(new Dictionary<string, object>
                    {
                        ["Operator"] = "==",
                        ["Operands"] = new List<object>
                        {
                            new { Attribute = "managementgroup", Operator = "==", Value = selectedMG.UsableId.ToString() }
                        }
                    });
                }
            }

            // If no filters are present, return an empty object ({}), which is the correct "no scope" payload.
            if (groupExprs.Count == 0)
                return new { };

            if (groupExprs.Count == 1)
                return groupExprs[0];

            return new Dictionary<string, object>
            {
                ["Operator"] = "OR",
                ["Operands"] = groupExprs
            };
        }


        private void RenderParameters(StackPanel panel, List<DexInstructionRunner.Models.Parameter> parameters)
        {
            panel.Children.Clear();

            foreach (var param in parameters ?? new List<DexInstructionRunner.Models.Parameter>())
            {
                var label = new TextBlock
                {
                    Text = param.Name,
                    Width = 100,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                if (!string.IsNullOrWhiteSpace(param.Description))
                    ToolTip.SetTip(label, param.Description);

                Control input;
                var allowedValues = param.Validation?.AllowedValues;

                if (allowedValues != null && allowedValues.Any())
                {
                    input = new ComboBox
                    {
                        ItemsSource = allowedValues,
                        Width = 200,
                        SelectedItem = param.DefaultValue ?? allowedValues.First()
                    };
                }
                else
                {
                    input = new TextBox
                    {
                        Width = 200,
                        Text = param.DefaultValue ?? ""
                    };
                }

                input.Tag = param.Name;

                // Wrap in Border to allow highlighting
                var border = new Border
                {
                    Child = input,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0),
                    Width = 200
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(label);
                row.Children.Add(border);
                panel.Children.Add(row);
            }
        }



        private async Task LoadResultsForInstructionAsync(InstructionHistoryItem selected)
        {
            LogToUI($"⏳ Loading results for instruction {selected.Name}...\n");
            _historyListBox.SelectedItem = selected;

            //ExperienceResultsPanel.Children.Clear();

            await RefreshResultsHeaderFromInstructionAsync(int.Parse(selected.Id));
            await LoadInstructionResultsAsync(int.Parse(selected.Id));
            UpdateView("Raw");
        }


        private void ResetResultsPagingState()
        {
            _resultsPrevRanges.Clear();
            _resultsPageIndex = 0;
            _resultsCurrentRange = "0;0";
            _resultsNextRange = null;
            _resultsPagingInitialized = false;

            _resultsOutstandingCount = 0;
            _resultsProgressCompletedSticky = false;
            UpdateResultsLoadingIndicator();
        }

        private void UpdateResultsTtlCountdownUi()
        {
            try
            {
                if (_resultsTtlCountdownText == null)
                    return;

                if (_currentInstructionTtlMinutes <= 0 || _currentInstructionRunStartedUtc == DateTime.MinValue)
                {
                    _resultsTtlCountdownText.IsVisible = false;
                    return;
                }

                var ttl = TimeSpan.FromMinutes(_currentInstructionTtlMinutes);
                var elapsed = DateTime.UtcNow - _currentInstructionRunStartedUtc;
                var remaining = ttl - elapsed;

                if (remaining <= TimeSpan.Zero)
                {
                    _resultsTtlCountdownText.Text = "TTL Remaining: 0m 00s (expired)";
                    _resultsTtlCountdownText.IsVisible = true;
                    return;
                }

                var totalMinutes = (int)Math.Floor(remaining.TotalMinutes);
                var seconds = Math.Max(0, remaining.Seconds);
                _resultsTtlCountdownText.Text = $"TTL Remaining: {totalMinutes}m {seconds:D2}s";
                _resultsTtlCountdownText.IsVisible = true;
            }
            catch
            {
                // ignore
            }
        }

        private TimeSpan? GetActiveResultsTtlRemaining()
        {
            try
            {
                // Prefer API-provided expiry (most accurate, especially for history selection).
                if (_activeResultsExpiresUtc.HasValue)
                {
                    var remainingFromApi = _activeResultsExpiresUtc.Value - DateTime.UtcNow;
                    return remainingFromApi;
                }

                // Fallback to local "run started" + TTL (only accurate for runs initiated in this session).
                if (_currentInstructionTtlMinutes > 0 && _currentInstructionRunStartedUtc != DateTime.MinValue)
                {
                    var ttl = TimeSpan.FromMinutes(_currentInstructionTtlMinutes);
                    var elapsed = DateTime.UtcNow - _currentInstructionRunStartedUtc;
                    return ttl - elapsed;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatTtlRemaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            // Keep it readable, but stable for long TTLs.
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays}d {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            return $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        private async Task LoadInstructionResultsAsync(int instructionId)
        {
            // Always reset paging when a new instruction is selected
            _lastInstructionId = instructionId;
            _resultsCoverageOverride = null;
            _resultsCoverageOverrideInstructionId = instructionId;
            _resultsTotalRowsExpected = 0;
            ResetResultsPagingState();

            // Get page size from UI (defaults to 500)
            _resultsPageSize = GetResultsPageSize();

            await LoadInstructionResultsPageAsync(instructionId, _resultsCurrentRange, resetPanels: true);
        }


        private async Task RefreshResultsHeaderFromInstructionAsync(int instructionId, bool log = false)
        {
            if (instructionId <= 0)
                return;

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
                return;

            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
                return;

            var endpoint = $"consumer/Instructions/{instructionId}";
            var url = $"https://{_selectedPlatform.Url}/{endpoint}";

            if (log)
                LogToUI($"ℹ️ Fetching instruction context: {url}\n");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var json = await ApiLogger.LogApiCallAsync(
                    label: "InstructionStatus",
                    endpoint: endpoint,
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync(url);
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: ""
                );

                JObject obj;
                try
                {
                    obj = JObject.Parse(json);
                }
                catch
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var nameTb = this.FindControl<TextBlock>("ResultsContextInstructionName");
                        var covTb = this.FindControl<TextBlock>("ResultsContextCoverage");
                        var filTb = this.FindControl<TextBlock>("ResultsContextFilters");

                        if (nameTb != null)
                        {
                            var readable = obj["ReadablePayload"]?.ToString();
                            var name = obj["Name"]?.ToString();
                            nameTb.Text = !string.IsNullOrWhiteSpace(readable) ? readable :
                                          (!string.IsNullOrWhiteSpace(name) ? name : "(instruction)");
                        }

                        if (covTb != null)
                        {
                            // If we already captured the intended coverage label for this instruction run (e.g. FQDN list),
                            // prefer it over whatever the status endpoint returns (which can be empty or a device count).
                            if (!string.IsNullOrWhiteSpace(_resultsCoverageOverride) && _resultsCoverageOverrideInstructionId == instructionId)
                            {
                                covTb.Text = _resultsCoverageOverride;
                            }
                            else
                            {
                                var readableScope = obj["ReadableScope"];
                                var scope = obj["Scope"];

                                var attr = readableScope?["Attribute"]?.ToString() ?? scope?["Attribute"]?.ToString();
                                var op = readableScope?["Operator"]?.ToString() ?? scope?["Operator"]?.ToString();
                                var val = readableScope?["Value"]?.ToString() ?? scope?["Value"]?.ToString();

                                string? covText = null;
                                if (!string.IsNullOrWhiteSpace(attr) && !string.IsNullOrWhiteSpace(op) && !string.IsNullOrWhiteSpace(val))
                                    covText = $"{attr} {op} {val}";

                                // If Scope is not populated (Targeted endpoint), fall back to Devices[] when available.
                                if (string.IsNullOrWhiteSpace(covText))
                                {
                                    try
                                    {
                                        if (obj["Devices"] is JArray devices)
                                        {
                                            var fqdns = devices
                                                .Select(x => (x?.ToString() ?? string.Empty).Trim())
                                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                                .Take(10)
                                                .ToList();

                                            if (fqdns.Count > 0)
                                                covText = $"FQDN == {string.Join(", ", fqdns)}";
                                        }
                                    }
                                    catch
                                    {
                                        // ignore
                                    }
                                }

                                covTb.Text = string.IsNullOrWhiteSpace(covText) ? "(coverage)" : covText;

                                // Persist the server-provided coverage so polling updates don't overwrite it with generic labels.
                                if (!string.IsNullOrWhiteSpace(covText))
                                {
                                    _resultsCoverageOverride = covText;
                                    _resultsCoverageOverrideInstructionId = instructionId;
                                }
                            }
                        }

                        if (filTb != null)
                        {
                            // Prefer ResultsFilter (what actually ran on the server)
                            filTb.Text = FormatResultsFilterSummaryFromToken(obj["ResultsFilter"]);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                });
            }
            catch (Exception ex)
            {
                if (log)
                {
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LogToUI($"⚠️ Failed to fetch instruction context: {ex.Message}\n");
                        });
                    }
                    catch { }
                }
            }
        }

        private static string FormatResultsFilterSummaryFromToken(Newtonsoft.Json.Linq.JToken? filterToken)
        {
            if (filterToken == null || filterToken.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                return "(none)";

            try
            {
                // Group: { Operator: "And", Operands: [...] }
                var groupOp = filterToken["Operator"]?.ToString();
                var operands = filterToken["Operands"] as Newtonsoft.Json.Linq.JArray;

                if (!string.IsNullOrWhiteSpace(groupOp) && operands != null && operands.Count > 0)
                {
                    var parts = operands
                        .Select(FormatResultsFilterSummaryFromToken)
                        .Where(s => !string.IsNullOrWhiteSpace(s) && s != "(none)")
                        .ToList();

                    if (parts.Count == 0)
                        return "(none)";

                    var joiner =
                        string.Equals(groupOp, "And", StringComparison.OrdinalIgnoreCase) ? " AND " :
                        string.Equals(groupOp, "Or", StringComparison.OrdinalIgnoreCase) ? " OR " :
                        " ";

                    return string.Join(joiner, parts);
                }

                // Operand: { Attribute, Operator, Value, DataType }
                var attr = filterToken["Attribute"]?.ToString() ?? "";
                var oper = filterToken["Operator"]?.ToString() ?? "";
                var val = filterToken["Value"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(attr))
                    return "(none)";

                // Normalize names -> symbols for display
                if (string.Equals(oper, "Equal", StringComparison.OrdinalIgnoreCase))
                    oper = "==";
                else if (string.Equals(oper, "NotEqual", StringComparison.OrdinalIgnoreCase))
                    oper = "!=";

                // Like patterns -> contains/starts/ends
                if (string.Equals(oper, "Like", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = val ?? "";
                    var starts = raw.StartsWith("%", StringComparison.Ordinal);
                    var ends = raw.EndsWith("%", StringComparison.Ordinal);
                    var inner = raw.Trim('%').Replace("\"", "");

                    if (starts && ends)
                        return $"{attr} contains \"{inner}\"";
                    if (!starts && ends)
                        return $"{attr} begins with \"{inner}\"";
                    if (starts && !ends)
                        return $"{attr} ends with \"{inner}\"";

                    return $"{attr} like \"{inner}\"";
                }

                // Standard compare: quote if it contains spaces or quotes
                if (!string.IsNullOrWhiteSpace(val) && (val.Contains(' ') || val.Contains('"')))
                {
                    var clean = val.Replace("\"", "");
                    return $"{attr} {oper} \"{clean}\"";
                }

                return $"{attr} {oper} {val}";
            }
            catch
            {
                return "(none)";
            }
        }

        private int GetResultsPageSize()
        {
            try
            {
                if (_resultsPageSizeCombo?.SelectedItem is ComboBoxItem cbi && cbi.Content != null)
                {
                    if (int.TryParse(cbi.Content.ToString(), out var n) && n > 0)
                        return n;
                }

                // Avalonia ComboBox can sometimes set SelectedItem to a string/int instead of ComboBoxItem.
                if (_resultsPageSizeCombo?.SelectedItem is string s && int.TryParse(s.Trim(), out var ns) && ns > 0)
                    return ns;
                if (_resultsPageSizeCombo?.SelectedItem is int ni && ni > 0)
                    return ni;
            }
            catch
            {
                // ignore
            }

            return _resultsPageSize > 0 ? _resultsPageSize : 20;
        }

        private async Task OnResultsPageSizeChangedAsync()
        {
            if (_lastInstructionId <= 0)
                return;

            _resultsPageSize = GetResultsPageSize();
            _resultsPrevRanges.Clear();
            _resultsPageIndex = 0;
            _resultsCurrentRange = "0;0";
            _resultsNextRange = null;

            await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
        }

        private async void ResultsPageSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            await OnResultsPageSizeChangedAsync();
        }

        private async void ResultsPrevPageButton_Click(object? sender, RoutedEventArgs e)
        {
            await NavigateResultsPageAsync(-1);
        }

        private async void ResultsNextPageButton_Click(object? sender, RoutedEventArgs e)
        {
            await NavigateResultsPageAsync(1);
        }

        private async void ResultsGoToPageButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var tb = this.FindControl<TextBox>("ResultsGoToPageTextBox");
                if (tb == null)
                    return;

                if (!int.TryParse((tb.Text ?? string.Empty).Trim(), out var targetPage))
                {
                    LogToUI("❌ Go To Page: enter a number.\n");
                    return;
                }

                if (targetPage < 1)
                    targetPage = 1;

                await GoToInstructionResultsPageAsync(targetPage);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Go To Page failed: {ex.Message}\n");
            }
        }

        private async Task<string?> FetchResultsNextRangeOnlyAsync(int instructionId, string startRange, int pageSize)
        {
            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
                return null;
            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
                return null;

            var url = $"https://{_selectedPlatform.Url}/consumer/Responses/{instructionId}";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            var postPayload = new
            {
                Filter = (object)null,
                Start = string.IsNullOrWhiteSpace(startRange) ? "0;0" : startRange,
                PageSize = pageSize
            };

            var content = new StringContent(JsonConvert.SerializeObject(postPayload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LogToUI($"❌ Error seeking results page: {response.StatusCode} - {json}\n");
                return null;
            }

            var parsed = JObject.Parse(json);
            var array = parsed["Responses"] as JArray;
            if (array == null || array.Count == 0)
                return null;

            return parsed["Range"]?.ToString();
        }

        private async Task GoToInstructionResultsPageAsync(int targetPage)
        {
            if (_lastInstructionId <= 0)
                return;

            // Page 1 is always range 0;0
            _resultsPageSize = GetResultsPageSize();

            // If we know the expected total, clamp to the max page so we don't seek past end.
            if (_resultsTotalRowsExpected > 0)
            {
                var maxPage = (int)Math.Ceiling(_resultsTotalRowsExpected / (double)Math.Max(1, _resultsPageSize));
                if (maxPage < 1) maxPage = 1;
                if (targetPage > maxPage)
                    targetPage = maxPage;
            }

            if (targetPage == 1)
            {
                _resultsPrevRanges.Clear();
                _resultsPageIndex = 0;
                _resultsCurrentRange = "0;0";
                _resultsNextRange = null;
                await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
                return;
            }

            // Seek forward from the beginning so we can land accurately on any page.
            var pageSize = _resultsPageSize;
            var ranges = new List<string>(); // ranges leading up to the target page
            var currentStart = "0;0";
            string? next;

            for (int page = 1; page < targetPage; page++)
            {
                next = await FetchResultsNextRangeOnlyAsync(_lastInstructionId, currentStart, pageSize);
                if (string.IsNullOrWhiteSpace(next))
                {
                    LogToUI($"⚠️ Reached end while seeking to page {targetPage}.\n");
                    targetPage = page; // last reachable page
                    break;
                }

                ranges.Add(currentStart);
                currentStart = next;
            }

            // Rebuild prev stack so Prev button works.
            _resultsPrevRanges.Clear();
            for (int i = 0; i < ranges.Count; i++)
                _resultsPrevRanges.Push(ranges[i]);

            _resultsCurrentRange = currentStart;
            _resultsPageIndex = Math.Max(0, targetPage - 1);
            await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
        }

        private async Task NavigateResultsPageAsync(int direction)
        {
            if (_lastInstructionId <= 0)
                return;

            if (direction < 0)
            {
                if (_resultsPrevRanges.Count == 0)
                    return;

                _resultsCurrentRange = _resultsPrevRanges.Pop();
                _resultsPageIndex = Math.Max(0, _resultsPageIndex - 1);
                await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
                return;
            }

            if (direction > 0)
            {
                if (string.IsNullOrWhiteSpace(_resultsNextRange))
                    return;

                if (!string.IsNullOrWhiteSpace(_resultsCurrentRange))
                    _resultsPrevRanges.Push(_resultsCurrentRange);

                _resultsCurrentRange = _resultsNextRange;
                _resultsPageIndex++;
                await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
            }
        }

        private object? BuildResultsServerFilterPayload()
        {
            // "Filter all (server)" is used when we want server-side filtering across all pages.
            // DexInstructionRunner already has a schema-driven run-scope filter builder; reuse it.
            // If no run-scope filter is active, return null so the server ignores it.
            try
            {
                // Use the same filter object we send when executing with Run Scope = Filtered.
                // This keeps the payload consistent with what the server expects (and what the UI summarizes).
                var rf = BuildRunResultsFilterObject(isFqdnTab: false);
                return rf;
            }
            catch
            {
                return null;
            }
        }

        private async Task TriggerResultsServerRefilterAsync()
        {
            try
            {
                if (_lastInstructionId <= 0)
                    return;

                // Reset paging to the first page when switching to server-side filtering.
                _resultsPrevRanges.Clear();
                _resultsPageIndex = 0;
                _resultsCurrentRange = "0;0";
                _resultsNextRange = null;

                // Don't tear down/rebuild the entire Results UI just to requery; this causes "new instruction" flashes
                // and can hide the Clear Filters button. We only need to clear the data state and reload.
                await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);
            }
            catch
            {
                // ignore
            }
        }

        private async Task LoadInstructionResultsPageAsync(int instructionId, string? startRange, bool resetPanels)
        {
            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("❌ No platform selected.\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("❌ Not authenticated.\n");
                return;
            }

            // Avoid clearing/rebuilding the Results UI on simple refreshes.
            // Only tear down the panels when the instruction actually changed.
            if (resetPanels)
            {
                if (_resultsUiBoundInstructionId != instructionId)
                {
                    ClearAllResultPanels();
                    _resultsUiBoundInstructionId = instructionId;
                }
                else
                {
                    // Data refresh for same instruction: clear data only (keep UI and filter controls)
                    _parsedResults.Clear();
                    _rawResults?.Clear();
                    _filteredResults?.Clear();
                }
            }

            _resultsPageLoading = true;
            UpdateResultsLoadingIndicator();

            try
            {
                _resultsPageSize = GetResultsPageSize();

                var url = $"https://{_selectedPlatform.Url}/consumer/Responses/{instructionId}";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var postPayload = new
                {
                    Filter = (_resultsServerFilterAllCheckBox?.IsChecked == true) ? BuildResultsServerFilterPayload() : null,
                    Start = string.IsNullOrWhiteSpace(startRange) ? "0;0" : startRange,
                    PageSize = _resultsPageSize
                };

                var content = new StringContent(JsonConvert.SerializeObject(postPayload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Error loading results page: {response.StatusCode} - {json}\n");
                    _resultsNextRange = null;
                    UpdateResultsPagingUi();
                    return;
                }

                var parsed = JObject.Parse(json);
                var array = parsed["Responses"] as JArray;
                var nextRange = parsed["Range"]?.ToString();

                LogToUI($"📄 Loaded page {_resultsPageIndex + 1}, PageSize: {_resultsPageSize}, Range: {postPayload.Start} → {nextRange}, Rows: {array?.Count ?? 0}\n");

                var pageRows = new List<Dictionary<string, string>>();

                if (array != null)
                {
                    foreach (var responseItem in array)
                    {
                        var row = new Dictionary<string, string>();

                        var fqdn = responseItem["Fqdn"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(fqdn))
                            row["Fqdn"] = fqdn;

                        var values = responseItem["Values"] as JObject;
                        if (values != null)
                        {
                            foreach (var prop in values.Properties())
                                row[prop.Name] = prop.Value?.ToString();
                        }

                        if (row.Count > 0)
                            pageRows.Add(row);
                    }
                }

                _rawResults = pageRows;
                _filteredResults = pageRows;

                // Clamp paging: if we received fewer than a full page, we're at the end.
                // Important: do NOT assume "end" just because fewer than PageSize rows were returned.
                // Some servers cap PageSize (e.g., 20) but still return a valid next Range.
                if (array == null || array.Count == 0)
                    nextRange = null;

                // Some servers return the same range for "Next" at end; treat that as no-next.
                if (!string.IsNullOrWhiteSpace(nextRange) && !string.IsNullOrWhiteSpace(postPayload.Start) &&
                    string.Equals(nextRange.Trim(), postPayload.Start.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    nextRange = null;
                }

                // If we know an expected total from progress statistics, clamp Next beyond the last page.
                if (_resultsTotalRowsExpected > 0)
                {
                    var maxPage = (int)Math.Ceiling(_resultsTotalRowsExpected / (double)Math.Max(1, _resultsPageSize));
                    if (maxPage <= 1)
                        nextRange = null;
                    else if ((_resultsPageIndex + 1) >= maxPage)
                        nextRange = null;
                }

                _resultsNextRange = nextRange;
                _resultsPagingInitialized = true;

                UpdateResultsPagingUi();

                // Preserve whichever view mode (Raw/Aggregated/Chart) the user currently has selected.
                var chartRadio = this.FindControl<RadioButton>("ChartViewRadioButton");
                var aggRadio = this.FindControl<RadioButton>("AggregatedViewRadioButton");

                var mode =
                    (chartRadio?.IsChecked == true) ? "Chart" :
                    (aggRadio?.IsChecked == true) ? "Aggregated" :
                    "Raw";

                await UpdateView(mode);

            }
            catch (Exception ex)
            {
                LogToUI($"❌ Exception during results page load: {ex.Message}\n");
                _resultsNextRange = null;
                UpdateResultsPagingUi();
            }
            finally
            {
                _resultsPageLoading = false;
                UpdateResultsLoadingIndicator();
            }
        }


        void UpdateResultsLoadingIndicator()
        {
            try
            {
                if (_loadingStatusPanel == null)
                    return;

                // Decide what to show. We do NOT mutate state flags here (no recursion).
                var show = false;
                var text = "";
                var indeterminate = false;
                var value = 0d;

                if (_resultsPageLoading)
                {
                    show = true;
                    text = "Fetching results...";
                    indeterminate = true;
                    value = 0;
                }
                else if (_resultsOutstandingCount > 0)
                {
                    show = true;
                    text = "Waiting on responses...";
                    indeterminate = true;
                    value = 0;
                    _resultsProgressCompletedSticky = false;
                }
                else if (_resultsProgressCompletedSticky)
                {
                    show = true;
                    text = "Completed";
                    indeterminate = false;
                    value = 100;
                }
                else
                {
                    // No page load in-flight and no outstanding responses.
                    // Keep a stable "Completed" state once we reach 0 outstanding at least once.
                    show = true;
                    text = "Completed";
                    indeterminate = false;
                    value = 100;
                    _resultsProgressCompletedSticky = true;
                }

                _loadingStatusPanel.IsVisible = show;

                if (_resultsLoadingProgressBar != null)
                {
                    _resultsLoadingProgressBar.IsVisible = show;
                    _resultsLoadingProgressBar.IsIndeterminate = indeterminate;
                    _resultsLoadingProgressBar.Value = value;
                }

                if (_resultsLoadingStatusText != null)
                {
                    _resultsLoadingStatusText.IsVisible = show;
                    _resultsLoadingStatusText.Text = text;
                }
            }
            catch
            {
                // no-op: loading indicator should never crash the UI
            }
        }

        private void UpdateResultsPagingUi()
        {
            try
            {
                var rowCount = _filteredResults?.Count ?? 0;
                var hasNext = !string.IsNullOrWhiteSpace(_resultsNextRange) && rowCount > 0;

                var startRow = rowCount > 0 ? (_resultsPageIndex * Math.Max(1, _resultsPageSize)) + 1 : 0;
                var endRow = (_resultsPageIndex * Math.Max(1, _resultsPageSize)) + rowCount;

                string totalText;
                if (_resultsTotalRowsExpected > 0)
                {
                    totalText = $" (Showing {endRow} of {_resultsTotalRowsExpected})";
                }
                else if (rowCount > 0)
                {
                    totalText = $" (Showing {endRow})";
                }
                else
                {
                    totalText = "";
                }

                var rangeText = rowCount > 0 ? $"{startRow}-{endRow}" : "0";

                if (_resultsPageInfoText != null)
                {
                    _resultsPageInfoText.Text =
                        $"Page {_resultsPageIndex + 1} (Rows: {rowCount}, Range: {rangeText})" +
                        totalText +
                        (hasNext ? "" : " (end)");
                }

                if (_resultsPrevPageButton != null)
                    _resultsPrevPageButton.IsEnabled = _resultsPrevRanges.Count > 0;

                if (_resultsNextPageButton != null)
                    _resultsNextPageButton.IsEnabled = hasNext;
            }
            catch
            {
                // ignore
            }
        }


        private void ApplyThemeAwareLogBoxStyling()
        {
            if (LogTextBox == null)
                return;

            var theme = this.ActualThemeVariant;

            IBrush foreground;
            IBrush background;
            IBrush border;

            if (theme == ThemeVariant.Dark)
            {
                background = Brushes.Black;
                foreground = Brushes.White;
                border = Brushes.Gray;
            }
            else
            {
                background = Brushes.White;
                foreground = Brushes.Black;
                border = Brushes.Gray;
            }

            LogTextBox.Background = background;
            LogTextBox.Foreground = foreground;
            LogTextBox.CaretBrush = foreground;
            LogTextBox.BorderBrush = border;
        }


        private void LogTextBox_GotFocus(object? sender, GotFocusEventArgs e)
        {
            ApplyThemeAwareLogBoxStyling();
        }

        private void LogTextBox_PointerEntered(object? sender, PointerEventArgs e)
        {
            ApplyThemeAwareLogBoxStyling();
        }



        private IBrush? TryGetThemeBrush(string resourceKey)
        {
            return Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true
                ? value as IBrush
                : null;
        }


        private object? TryFindResource(string key, ThemeVariant theme)
        {
            if (this.Resources.TryGetResource(key, theme, out var resource))
                return resource;
            return null;
        }


        private async void HistoryListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            // Debounce: DoubleTapped can fire twice quickly depending on input/device
            var now = DateTime.UtcNow;
            if ((now - _lastHistoryDoubleTapUtc).TotalMilliseconds < 350)
                return;
            _lastHistoryDoubleTapUtc = now;

            if (_historyListBox.SelectedItem is InstructionHistoryItem selected && selected != null)
            {
                var tabControl = this.FindControl<TabControl>("MainTabControl");
                if (tabControl != null)
                    tabControl.SelectedIndex = 2;

                _currentInstructionId = selected.Id;
                string instructionId = selected.Id;

                // Canonical active instruction id for Results live polling.
                if (int.TryParse(instructionId, out var selectedIid))
                {
                    _activeResultsInstructionId = selectedIid;
                    _lastInstructionId = selectedIid;
                }
                else
                {
                    _activeResultsInstructionId = -1;
                }

                _activeResultsExpiresUtc = null;
                _lastProgressSentCount = -1;
                _lastProgressReceivedCount = -1;
                _lastProgressOutstandingCount = -1;
                _lastPeriodicResultsRefreshUtc = DateTime.MinValue;
                _lastAutoResultsRefreshUtc = DateTime.MinValue;

                var status = GetStatusText(selected.Status ?? "");

                var statusTextBlock = this.FindControl<TextBlock>("ResultsStatusText");
                if (statusTextBlock != null)
                {
                    statusTextBlock.Text = $"Status: {status}";
                    statusTextBlock.Foreground = status switch
                    {
                        string s when s.Contains("Completed", StringComparison.OrdinalIgnoreCase) => Brushes.LightGreen,
                        string s when s.Contains("Failed", StringComparison.OrdinalIgnoreCase) => Brushes.Red,
                        string s when s.Contains("In Progress", StringComparison.OrdinalIgnoreCase) => Brushes.Orange,
                        string s when s.Contains("Cancelling", StringComparison.OrdinalIgnoreCase) => Brushes.Goldenrod,
                        string s when s.Contains("Expired", StringComparison.OrdinalIgnoreCase) => Brushes.Gray,
                        _ => Brushes.LightBlue
                    };
                }

                // ✅ Update TTL countdown every tick while the instruction is active
                UpdateResultsTtlCountdownUi();

                this.FindControl<StackPanel>("ResultsPanel")?.Children.Clear();
                this.FindControl<RadioButton>("RawViewRadioButton")!.IsChecked = true;

                // Show "no results" for expired or failed
                if (status.Contains("Expired", StringComparison.OrdinalIgnoreCase))
                {
                    // ✅ Only prompt once per instruction id, and prevent double-dialog
                    var rerun = await PromptExpiredRerunOnceAsync(instructionId);
                    if (rerun)
                    {
                        try
                        {
                            var newRunId = await RerunInstructionAsync();
                            if (newRunId == null || newRunId <= 0)
                            {
                                LogToUI("❌ Rerun did not return a new run/execution id.");
                                return;
                            }

                            await ActivateResultsForRunAsync(newRunId.Value);
                        }
                        catch (Exception ex)
                        {
                            LogToUI($"❌ Rerun failed: {ex.Message}");
                        }
                        return;
                    }




                    var resultsPanel = this.FindControl<StackPanel>("ResultsPanel");
                    resultsPanel?.Children.Add(new TextBlock
                    {
                        Text = $"❌ This instruction is '{status}'. No results available.",
                        Foreground = Brushes.LightGray,
                        FontStyle = FontStyle.Italic,
                        Margin = new Thickness(10)
                    });
                    return;
                }

                if (status.Contains("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var resultsPanel = this.FindControl<StackPanel>("ResultsPanel");
                    resultsPanel?.Children.Add(new TextBlock
                    {
                        Text = $"❌ This instruction is '{status}'. No results available.",
                        Foreground = Brushes.LightGray,
                        FontStyle = FontStyle.Italic,
                        Margin = new Thickness(10)
                    });
                    return;
                }

                // ✅ Only load results if the instruction is still the current one
                await LoadHistoryItemResultsAsync(selected);
                _resultsStatusPollRunId++;
                var runId = _resultsStatusPollRunId;

                _ = RefreshInstructionStatusAsync(instructionId, runId);

                if (int.TryParse(instructionId, out var iid))
                {
                    _ = ShowInstructionProgressAsync(iid, log: false);
                }
            }
        }


        private async Task<bool> PromptExpiredRerunOnceAsync(string instructionId)
        {
            if (string.IsNullOrWhiteSpace(instructionId))
                return false;

            // Already prompted for this instruction id in this session
            if (_expiredRerunPromptShownForInstructionIds.Contains(instructionId))
                return false;

            // If a prompt is already being shown, don't show another
            if (_expiredRerunPromptInFlight)
                return false;

            _expiredRerunPromptInFlight = true;
            try
            {
                // Mark early to prevent double prompts from re-entrancy
                _expiredRerunPromptShownForInstructionIds.Add(instructionId);

                var rerun = await ShowYesNoDialogAsync(
                    "Results expired",
                    "This instruction has expired and results are no longer available.\n\nWould you like to rerun it?",
                    "Rerun",
                    "Cancel");

                return rerun;
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Expired rerun prompt failed: {ex.Message}"); } catch { }
                return false;
            }
            finally
            {
                _expiredRerunPromptInFlight = false;
            }
        }

        private int _resultsStatusPollRunId = 0;
        // Throttle UI logging for progress polling so we can confirm the stats endpoint is being hit
        private int _progressStatsPollCounter = 0;

        private async Task RefreshInstructionStatusAsync(string instructionId, int runId)
        {
            while (true)
            {
                try
                {
                    // If a newer poll cycle started (user switched instructions), stop.
                    if (runId != _resultsStatusPollRunId)
                        return;

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                    client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                    string url = $"https://{_selectedPlatform?.Url}/consumer/Instructions/{instructionId}";
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();

                    // If user switched instructions, stop.
                    if (_currentInstructionId != instructionId || runId != _resultsStatusPollRunId)
                        return;

                    var item = JObject.Parse(json);

                    var rawStatus = item["Status"]?.ToString() ?? "Unknown";
                    var statusText = GetStatusText(rawStatus);

                    var workflowState = item["WorkflowState"]?.Value<int?>() ?? -1;
                    var executionId = item["Id"]?.Value<long?>() ?? 0L;
                    var instructionName = item["Name"]?.ToString();

                    // 2FA prompt for actions when WorkflowState == 11 (Authenticating)
                    if (workflowState == 11 && executionId > 0)
                    {
                        if (!_authPromptShownForInstructionIds.Contains(executionId))
                        {
                            _authPromptShownForInstructionIds.Add(executionId);

                            try
                            {
                                LogToUI($"🔐 Instruction {executionId} is awaiting authentication.\n");

                                var submitted = await PromptForInstructionAuthenticationAsync(executionId, instructionName);
                                if (!submitted)
                                {
                                    LogToUI($"❌ Authentication prompt was canceled for instruction {executionId}.\n");
                                }
                                else
                                {
                                    LogToUI($"✅ Authentication code submitted for instruction {executionId}.\n");
                                }
                            }
                            finally
                            {
                                // allow another prompt later only if it re-enters auth state in a future run
                                _authPromptShownForInstructionIds.Remove(executionId);
                            }
                        }
                    }

                    // Try to derive instruction expiration from the status payload (preferred over relying on the UI TTL field)
                    DateTime? expiresUtc = null;
                    try
                    {
                        var expiryToken = item["ExpirationTimestampUtc"]
                                         ?? item["ExpiryTimestampUtc"]
                                         ?? item["ExpiresAtUtc"]
                                         ?? item["ExpiresUtc"]
                                         ?? item["ExpireTimestampUtc"]
                                         ?? item["ExpiryUtc"];

                        var expiryStr = expiryToken?.ToString();
                        if (!string.IsNullOrWhiteSpace(expiryStr) && DateTime.TryParse(expiryStr, out var parsedExpiry))
                        {
                            expiresUtc = DateTime.SpecifyKind(parsedExpiry, DateTimeKind.Utc);
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    // Capture API-provided expiry for the active Results instruction.
                    if (expiresUtc.HasValue &&
                        _activeResultsInstructionId > 0 &&
                        int.TryParse(instructionId, out var iidForExpiry) &&
                        iidForExpiry == _activeResultsInstructionId)
                    {
                        _activeResultsExpiresUtc = expiresUtc;
                    }

                    // Update the status block
                    var statusTextBlock = this.FindControl<TextBlock>("ResultsStatusText");
                    if (statusTextBlock != null)
                    {
                        bool isActiveResults = _activeResultsInstructionId > 0 &&
                                               int.TryParse(instructionId, out var iidForStatus) &&
                                               iidForStatus == _activeResultsInstructionId;

                        bool force = statusText.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                                     statusText.Contains("Expired", StringComparison.OrdinalIgnoreCase) ||
                                     statusText.Contains("Cancelling", StringComparison.OrdinalIgnoreCase) ||
                                     workflowState == 11;

                        if (!isActiveResults || force)
                        {
                            statusTextBlock.Text = workflowState == 11
                                ? "Status: Awaiting Authentication"
                                : $"Status: {statusText}";
                        }

                        statusTextBlock.Foreground = workflowState == 11
                            ? Brushes.Goldenrod
                            : statusText switch
                            {
                                string s when s.Contains("Completed", StringComparison.OrdinalIgnoreCase) => Brushes.LightGreen,
                                string s when s.Contains("Failed", StringComparison.OrdinalIgnoreCase) => Brushes.Red,
                                string s when s.Contains("In Progress", StringComparison.OrdinalIgnoreCase) => Brushes.Orange,
                                string s when s.Contains("Cancelling", StringComparison.OrdinalIgnoreCase) => Brushes.Goldenrod,
                                string s when s.Contains("Expired", StringComparison.OrdinalIgnoreCase) => Brushes.Gray,
                                _ => Brushes.LightBlue
                            };
                    }

                    // TTL countdown
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (_resultsTtlCountdownText != null && expiresUtc.HasValue)
                            {
                                var remainingUi = expiresUtc.Value - DateTime.UtcNow;
                                if (remainingUi <= TimeSpan.Zero)
                                {
                                    _resultsTtlCountdownText.Text = "TTL Remaining: 0m 00s (expired)";
                                    _resultsTtlCountdownText.IsVisible = true;
                                }
                                else
                                {
                                    var totalMinutes = (int)Math.Floor(remainingUi.TotalMinutes);
                                    var seconds = Math.Max(0, remainingUi.Seconds);
                                    _resultsTtlCountdownText.Text = $"TTL Remaining: {totalMinutes}m {seconds:D2}s";
                                    _resultsTtlCountdownText.IsVisible = true;
                                }
                            }
                            else
                            {
                                UpdateResultsTtlCountdownUi();
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }, DispatcherPriority.Background);

                    // Update the progress summary every tick
                    if (int.TryParse(instructionId, out var iid))
                    {
                        var tick = System.Threading.Interlocked.Increment(ref _progressStatsPollCounter);
                        var enableLog = (tick == 1) || (tick % 6) == 0;
                        await ShowInstructionProgressAsync(iid, log: enableLog);
                    }

                    // Keep polling while TTL is still active
                    var remaining = GetActiveResultsTtlRemaining();
                    bool ttlActive = remaining.HasValue && remaining.Value > TimeSpan.Zero;

                    if (!ttlActive)
                        return;

                    var delayMs = 15000;

                    var sinceIncrease = _lastResultsReceivedIncreaseUtc == DateTime.MinValue
                        ? TimeSpan.Zero
                        : (DateTime.UtcNow - _lastResultsReceivedIncreaseUtc);

                    if (sinceIncrease < TimeSpan.FromSeconds(30))
                        delayMs = 5000;
                    else if (sinceIncrease < TimeSpan.FromMinutes(2))
                        delayMs = 15000;
                    else if (sinceIncrease < TimeSpan.FromMinutes(5))
                        delayMs = 30000;
                    else
                        delayMs = 60000;

                    if (statusText.Contains("In Progress", StringComparison.OrdinalIgnoreCase) ||
                        statusText.Contains("Cancelling", StringComparison.OrdinalIgnoreCase) ||
                        workflowState == 11)
                    {
                        delayMs = Math.Min(delayMs, 5000);
                    }

                    await Task.Delay(delayMs);

                    if (_currentInstructionId != instructionId || runId != _resultsStatusPollRunId)
                        return;
                }
                catch (Exception ex)
                {
                    LogToUI($"❌ Failed to refresh status: {ex.Message}\n");
                    await Task.Delay(5000);
                }
            }
        }

        private async Task<bool> PromptForInstructionAuthenticationAsync(long executionId, string? instructionName = null)
        {
            try
            {
                var dialog = new InstructionAuthCodeWindow(executionId, instructionName);
                var code = await dialog.ShowDialog<string?>(this);

                if (string.IsNullOrWhiteSpace(code))
                    return false;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                string url = $"https://{_selectedPlatform?.Url}/consumer/Authentication/Instruction/Token";

                var payload = new
                {
                    Token = code.Trim(),
                    Id = executionId
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Authentication submission failed for instruction {executionId}: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}\n");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to submit authentication code for instruction {executionId}: {ex.Message}\n");
                return false;
            }
        }

        private void HistoryListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_historyListBox.SelectedItem is InstructionHistoryItem selected)
            {
                if (int.TryParse(selected.Id, out int id))
                    _lastInstructionId = id;

                _lastInstructionDefinition = _instructionDefinitions.FirstOrDefault(i => i.Id == selected.DefinitionId);

                LogToUI($"ℹ️ Selected instruction ID for actions: {_lastInstructionId}\n");
            }
        }



        private async Task FetchProjectedStatsAsync(int instructionDefinitionId, object scopeQuery, object expression)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var payload = new
                {
                    instructionDefinitionId,
                    scopeQuery,
                    Id = instructionDefinitionId,
                    Expression = expression
                };
                var payloadJson = JsonConvert.SerializeObject(payload);
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                var json = await ApiLogger.LogApiCallAsync(
                    label: "ProjectedInstructionStatistics",
                    endpoint: "Consumer/SystemStatistics/ProjectedInstructionStatistics",
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync($"https://{_selectedPlatform?.Url}/Consumer/SystemStatistics/ProjectedInstructionStatistics", content);

                        if (!response.IsSuccessStatusCode)
                        {
                            LogToUI($"❌ Projected stats failed: {response.StatusCode}\n");
                        }

                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: payloadJson
                );

                var stats = JObject.Parse(json);
                RenderProjectedStatsPanel(stats);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching projected stats: {ex.Message}\n");
            }
        }


        private void RenderProjectedStatsPanel(JObject stats)
        {
            var panel = this.FindControl<StackPanel>("ProjectedStatsPanel");
            panel.Children.Clear();

            var title = new TextBlock
            {
                Text = "📊 Projected Stats",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 10, 0, 5)
            };
            panel.Children.Add(title);

            var rows = new[]
            {
        ("EstimatedCount", "Total Devices"),
        ("EstimatedSuccessRespondents", "Online Devices"),
        ("EstimatedRowInserts", "Expected Rows"),
        ("EstimatedAvgExecTime", "Avg Execution Time"),
        ("EstimatedBytesReceived", "Data Received")
    };

            foreach (var (key, label) in rows)
            {
                var val = stats[key]?.ToString();
                if (val != null)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new TextBlock { Text = label + ":", Width = 160 });
                    row.Children.Add(new TextBlock { Text = val });
                    panel.Children.Add(row);
                }
            }
        }


        private void RenderResultsList(List<Dictionary<string, string>> data)
        {
            _filteredResults = data;

            var resultsPanel = this.FindControl<StackPanel>("ResultsPanel");
            resultsPanel.Children.Clear();

            if (data == null || data.Count == 0)
            {
                LogToUI("⚠️ No results to display.\n");
                return;
            }

            var columns = data.SelectMany(row => row.Keys).Distinct().ToList();
            var filterBoxes = new Dictionary<string, TextBox>();
            _columnFilters = filterBoxes;

            // Compute stable widths for the current page so header + rows align, while still
            // allowing wider columns when values are longer.
            ComputeAndStoreGridColumnWidths(
                columns,
                columns, // display names
                data.Select(r => (IReadOnlyDictionary<string, string>)r),
                GridWidthContext.Results);

            var listArea = new StackPanel(); // only this scrolls

            // Create header grid
            var headerGrid = new Grid
            {
                ColumnDefinitions = GenerateGridColumns(columns, GridWidthContext.Results),
                Margin = new Thickness(0, 0, 0, 5)
            };

            // Add spacer for checkbox column
            var spacer = new Border { Width = 30, Background = Brushes.Transparent };
            Grid.SetColumn(spacer, 0);
            headerGrid.Children.Add(spacer);

            for (int i = 0; i < columns.Count; i++)
            {
                var colName = columns[i];

                _resultsGridColumnWidths.TryGetValue(colName, out var colWidth);
                if (colWidth <= 0) colWidth = GridDataColumnMinWidth;

                var innerGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(GridColumnGap / 2, 0, GridColumnGap / 2, 0)
                };

                var filterBox = new TextBox
                {
                    Watermark = $"Filter {colName}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(0),
                    Padding = new Thickness(4, 2, 4, 2)
                };

                // Restore prior filter text for this column (auto-refresh should not blow away filters).
                if (_resultsFilterText.TryGetValue(colName, out var savedFilter) && !string.IsNullOrEmpty(savedFilter))
                {
                    filterBox.Text = savedFilter;
                }

                // Keep filter state updated as user types.
                filterBox.GetObservable(TextBox.TextProperty)
                         .Throttle(TimeSpan.FromMilliseconds(150))
                         .Subscribe(t =>
                         {
                             var txt = t?.Trim() ?? string.Empty;
                             if (string.IsNullOrEmpty(txt))
                                 _resultsFilterText.Remove(colName);
                             else
                                 _resultsFilterText[colName] = txt;
                         });
                filterBoxes[colName] = filterBox;
                Grid.SetColumn(filterBox, 0);

                var sortButton = new Button
                {
                    Content = "⇅",
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                sortButton.Click += (s, e) => ToggleSortOrder(colName, listArea);
                Grid.SetColumn(sortButton, 1);

                innerGrid.Children.Add(filterBox);
                innerGrid.Children.Add(sortButton);

                Grid.SetColumn(innerGrid, i + 1); // shift by 1 for checkbox column
                headerGrid.Children.Add(innerGrid);
            }

            // Toolbar
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Spacing = 10,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var selectAllCheckbox = new CheckBox { Content = "Select All FQDNs" };
            selectAllCheckbox.Checked += (_, __) =>
            {
                foreach (var cb in _fqdnCheckboxes.Values) cb.IsChecked = true;
            };
            selectAllCheckbox.Unchecked += (_, __) =>
            {
                foreach (var cb in _fqdnCheckboxes.Values) cb.IsChecked = false;
            };

            var clearFiltersButton = new Button
            {
                Content = "Clear Filters",
                Padding = new Thickness(8, 2, 8, 2)
            };
            clearFiltersButton.Click += (_, __) =>
            {
                if (_columnFilters == null || _columnFilters.Count == 0)
                    return;

                _suppressResultsAutoFilterApply = true;
                try
                {
                    foreach (var box in _columnFilters.Values)
                        box.Text = string.Empty;

                    _resultsFilterText.Clear();
                }
                finally
                {
                    _suppressResultsAutoFilterApply = false;
                }

                // If your Results filtering is applied via ApplyFilters(...) with listArea:
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ApplyFilters(_columnFilters, _filteredResults.SelectMany(r => r.Keys).Distinct().ToList(), listArea);
                });
            };

            var sendToListButton = new Button
            {
                Content = "📤 Send to List",
                Width = 140,
                Padding = new Thickness(8, 2, 8, 2)
            };
            // sendToListButton.Click += OnSendSelectedFqdnsToList_Click;

            toolbar.Children.Add(selectAllCheckbox);
            toolbar.Children.Add(clearFiltersButton);
            // Runner: no global FQDN list feature, hide Send to List.
            // toolbar.Children.Add(sendToListButton);

            var verticalScrollViewer = new ScrollViewer
            {
                Content = listArea,
                Height = 450,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Keep header visible while vertical scrolling rows, but share horizontal scrolling between header + rows.
            var horizontalStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6
            };
            horizontalStack.Children.Add(new Border
            {
                Padding = new Thickness(4, 0, 4, 0),
                Child = headerGrid
            });
            horizontalStack.Children.Add(verticalScrollViewer);

            var horizontalScrollViewer = new ScrollViewer
            {
                Content = horizontalStack,
                Height = 520, // header + rows viewport
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            container.Children.Add(toolbar);
            container.Children.Add(horizontalScrollViewer);
            resultsPanel.Children.Add(container);
            foreach (var row in data)
            {
                var rowGrid = new Grid
                {
                    ColumnDefinitions = GenerateGridColumns(columns, GridWidthContext.Results),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                if (row.TryGetValue("Fqdn", out var fqdnValue))
                {
                    if (!_fqdnCheckboxes.ContainsKey(fqdnValue))
                        _fqdnCheckboxes[fqdnValue] = cb;
                }
                Grid.SetColumn(cb, 0);
                rowGrid.Children.Add(cb);

                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    var cellText = row.TryGetValue(col, out var value) ? value : "";

                    bool isFqdn = string.Equals(col, "Fqdn", StringComparison.OrdinalIgnoreCase) || i == 0;

                    if (isFqdn && !string.IsNullOrWhiteSpace(cellText))
                    {
                        var textBlock = new TextBlock
                        {
                            Text = cellText,
                            Foreground = Brushes.Blue,
                            TextDecorations = TextDecorations.Underline,
                            Cursor = new Cursor(StandardCursorType.Hand),
                            Margin = new Thickness(4 + (GridColumnGap / 2), 2, 4 + (GridColumnGap / 2), 2),
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        textBlock.Tapped += (_, __) =>
                        {
                            LogToUI($"🔗 Clicked FQDN: {cellText}");
                            ShowDevicePanel(cellText);
                        };


                        //  LogToUI($"✅ Added clickable FQDN: {cellText}\n");

                        Grid.SetColumn(textBlock, i + 1);
                        rowGrid.Children.Add(textBlock);
                    }


                    else
                    {
                        var textBlock = new TextBlock
                        {
                            Text = cellText,
                            Margin = new Thickness(4 + (GridColumnGap / 2), 2, 4 + (GridColumnGap / 2), 2),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(textBlock, i + 1);
                        rowGrid.Children.Add(textBlock);
                    }
                }

                var rowBorder = new Border
                {
                    Padding = new Thickness(4, 0, 4, 0),
                    Child = rowGrid
                };

                // 👇 ADD THIS
                rowBorder.DoubleTapped += (_, __) =>
                {
                    if (row.TryGetValue("Fqdn", out var fqdn) && !string.IsNullOrWhiteSpace(fqdn))
                        ShowDevicePanel(fqdn);
                };

                listArea.Children.Add(rowBorder);

            }

            _columnFilters = filterBoxes;

            foreach (var filterBox in filterBoxes.Values)
            {
                filterBox.GetObservable(TextBox.TextProperty)
                         .Throttle(TimeSpan.FromMilliseconds(300))
                         .Skip(1)
                         .Subscribe(__ =>
                         {
                             Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                             {
                                 if (_suppressResultsAutoFilterApply)
                                     return;

                                 // Now we're on UI thread; safe to read IsChecked
                                 if (_resultsServerFilterAllCheckBox?.IsChecked == true)
                                 {
                                     await TriggerResultsServerRefilterAsync();
                                     return;
                                 }

                                 ApplyFilters(filterBoxes, columns, listArea);
                             });
                         });
            }

            ApplyFilters(filterBoxes, columns, listArea);
        }







        // Keep track of the current sort order for each column (ascending/descending)
        private Dictionary<string, bool> columnSortOrder = new Dictionary<string, bool>();

        private void ToggleSortOrder(string column, StackPanel listArea)
        {
            // If the column doesn't have a sort order, set it to ascending
            if (!columnSortOrder.ContainsKey(column))
            {
                columnSortOrder[column] = true;
            }
            else
            {
                // Toggle between ascending and descending
                columnSortOrder[column] = !columnSortOrder[column];
            }

            // Sort the data based on the column and sort order
            if (columnSortOrder[column])
            {
                // Ascending order
                _filteredResults = _filteredResults.OrderBy(row => row[column]).ToList();
            }
            else
            {
                // Descending order
                _filteredResults = _filteredResults.OrderByDescending(row => row[column]).ToList();
            }

            // Reapply filters and display the sorted data
            ApplyFilters(_columnFilters, _filteredResults.SelectMany(row => row.Keys).Distinct().ToList(), listArea);
        }

        // NOTE: Results + Experience tabs build a header grid and N row grids.
        // If the data columns are Auto-sized, each row grid can measure to a different width,
        // causing header/row misalignment. We keep widths stable per-render by computing a
        // fixed width per column (based on the current page of data) and using that for both
        // the header + rows.
        private const double GridDataColumnMinWidth = 140;
        private const double GridDataColumnMaxWidth = 1200;
        private const double GridColumnGap = 10; // visual gap between columns (header + rows)

        private readonly Dictionary<string, double> _resultsGridColumnWidths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _experienceGridColumnWidths = new(StringComparer.OrdinalIgnoreCase);

        private enum GridWidthContext
        {
            Results,
            Experience
        }

        private ColumnDefinitions GenerateGridColumns(IReadOnlyList<string> columns, GridWidthContext ctx)
        {
            var widths = ctx == GridWidthContext.Results ? _resultsGridColumnWidths : _experienceGridColumnWidths;

            // Column 0 is a small fixed column used for the checkbox (or a spacer).
            var columnDefs = new ColumnDefinitions
            {
                new ColumnDefinition { Width = new GridLength(30), MinWidth = 30 }
            };

            for (int i = 0; i < columns.Count; i++)
            {
                var name = columns[i];
                if (!widths.TryGetValue(name, out var w) || w <= 0)
                    w = GridDataColumnMinWidth;

                columnDefs.Add(new ColumnDefinition
                {
                    Width = new GridLength(w),
                    MinWidth = Math.Min(GridDataColumnMinWidth, w)
                });
            }

            return columnDefs;
        }

        // Back-compat for other areas that still call the original signature.
        private ColumnDefinitions GenerateGridColumns(int dataColumnCount)
        {
            var cols = new List<string>(dataColumnCount);
            for (int i = 0; i < dataColumnCount; i++)
                cols.Add($"C{i}");

            for (int i = 0; i < cols.Count; i++)
                _resultsGridColumnWidths[cols[i]] = GridDataColumnMinWidth;

            return GenerateGridColumns(cols, GridWidthContext.Results);
        }

        private void ComputeAndStoreGridColumnWidths(
            IReadOnlyList<string> columns,
            IEnumerable<string> headerDisplayNames,
            IEnumerable<IReadOnlyDictionary<string, string>> rows,
            GridWidthContext ctx)
        {
            var widths = ctx == GridWidthContext.Results ? _resultsGridColumnWidths : _experienceGridColumnWidths;
            // Results are often paged (especially for large instruction outputs).
            // To avoid columns "jittering" (shrinking/growing) between page loads and to ensure we
            // eventually size to the widest values encountered, we only clear widths when the column
            // set changes (e.g., new instruction/run). Experience can change its selected measures
            // frequently, so we always recompute from scratch there.
            if (ctx == GridWidthContext.Experience)
            {
                widths.Clear();
            }
            else
            {
                if (widths.Count == 0)
                {
                    // first run for this column set
                }
                else
                {
                    // If the column set changed, reset.
                    bool same = widths.Count == columns.Count;
                    if (same)
                    {
                        for (int i = 0; i < columns.Count; i++)
                        {
                            if (!widths.ContainsKey(columns[i])) { same = false; break; }
                        }
                    }
                    if (!same)
                        widths.Clear();
                }
            }

            // Use a cheap-but-stable measurement for the current visible page.
            // We include the header display names so filter box watermarks don't truncate immediately.
            var headerList = headerDisplayNames.ToList();

            int maxSampleRows = 60;
            bool fastMeasure = false;

            // Selecting many measures in Experience can create a very wide grid; keep width computation cheap to avoid UI hangs.
            if (columns.Count > 40 || (ctx == GridWidthContext.Experience && columns.Count > 28))
            {
                maxSampleRows = 8;
                fastMeasure = true;
            }

            Func<string, double> measure = fastMeasure ? MeasureTextWidthHeuristic : MeasureTextWidth;

            // Even in "fast" mode, always compute stable widths for the leading identity columns.
            // These are the columns users visually anchor on (e.g., FQDN/Setting/Value), and if they
            // under-measure the drift looks like column misalignment.
            //
            // IMPORTANT: keep this bounded to avoid UI stalls. Visible pages are typically small
            // (20/250/etc). We cap full-scan rows just in case.
            const int maxFullScanRows = 350;

            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var best = GridDataColumnMinWidth;

                if (i < headerList.Count)
                    best = Math.Max(best, measure(headerList[i]) + 34); // padding + sort button

                // Sample rows to prevent UI stalls.
                // In fast mode, the first few columns get a fuller scan so the left side stays visually consistent.
                int sampled = 0;
                int limit = maxSampleRows;

                // i==0 is almost always the primary identity column (FQDN in both tabs).
                // Results also commonly has long strings in the next columns (e.g., Setting/Value).
                if (i == 0 || (ctx == GridWidthContext.Results && i <= 2) || (ctx == GridWidthContext.Experience && i <= 1))
                {
                    limit = maxFullScanRows;
                }

                foreach (var r in rows)
                {
                    if (sampled++ >= limit) break;
                    if (r.TryGetValue(col, out var v) && !string.IsNullOrEmpty(v))
                        best = Math.Max(best, measure(v) + 18);
                }

                best = Math.Clamp(best, GridDataColumnMinWidth, GridDataColumnMaxWidth);

                if (widths.TryGetValue(col, out var existing))
                    widths[col] = Math.Max(existing, best);
                else
                    widths[col] = best;
            }
        }

        private static double MeasureTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            try
            {
                // Avoid Avalonia version differences in FormattedText API by measuring a lightweight TextBlock.
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap
                };

                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return tb.DesiredSize.Width;
            }
            catch
            {
                // Fallback heuristic (stable even if text measuring isn't available at runtime).
                return Math.Min(900, text.Length * 7.2);
            }
        }



        private static double MeasureTextWidthHeuristic(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            // Fast, allocation-free approximation used when rendering very wide grids (e.g., selecting many Experience measures).
            // Keeps the UI responsive and still yields stable widths between header and rows.
            var w = text.Length * 7.2;
            return Math.Min(1400, w);
        }


        // Filter syntax (shared by Results + Experience):
        //  - Numeric comparisons: >, >=, <, <=, =, !=  (e.g. ">= 90", "<5", "!=0")
        //  - Text operators: "=foo" (exact), "^foo" (begins with), "foo*" (begins with)
        //  - Default for text: contains (case-insensitive)
        private static bool MatchesFilter(string value, string filterText)
        {
            value ??= string.Empty;
            filterText ??= string.Empty;

            var f = filterText.Trim();
            if (f.Length == 0)
                return true;

            // If the user typed a bare number (e.g. "4") and the cell is numeric, treat it as an equality filter
            // so "4" does NOT match "14"/"24". Users can still force substring matching by prefixing with ~,
            // e.g. "~4".
            if (IsBareNumericFilter(f) && TryParseDoubleLoose(value, out var lhsBare) && TryParseDoubleLoose(f, out var rhsBare))
            {
                return Math.Abs(lhsBare - rhsBare) < 0.0000001;
            }

            // Force "contains" for numeric/text via ~ prefix.
            if (f.StartsWith("~", StringComparison.Ordinal))
            {
                var needle = f[1..].Trim();
                return value.Contains(needle, StringComparison.OrdinalIgnoreCase);
            }

            // Numeric comparisons if the filter starts with a comparison operator.
            if (TryParseComparisonFilter(f, out var op, out var rhs))
            {
                if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lhs) &&
                    !double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out lhs))
                {
                    return false;
                }

                return op switch
                {
                    ">" => lhs > rhs,
                    ">=" => lhs >= rhs,
                    "<" => lhs < rhs,
                    "<=" => lhs <= rhs,
                    "=" => Math.Abs(lhs - rhs) < 0.0000001,
                    "==" => Math.Abs(lhs - rhs) < 0.0000001,
                    "!=" => Math.Abs(lhs - rhs) >= 0.0000001,
                    _ => false
                };
            }

            // Text: exact match
            if (f.StartsWith("=", StringComparison.Ordinal) && !f.StartsWith("==", StringComparison.Ordinal))
            {
                var needle = f[1..].Trim();
                return string.Equals(value, needle, StringComparison.OrdinalIgnoreCase);
            }

            // Text: begins-with
            if (f.StartsWith("^", StringComparison.Ordinal))
            {
                var needle = f[1..].Trim();
                return value.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            }

            if (f.EndsWith("*", StringComparison.Ordinal) && f.Length > 1)
            {
                var needle = f[..^1].Trim();
                return value.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            }

            // Default: contains
            return value.Contains(f, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseComparisonFilter(string filter, out string op, out double rhs)
        {
            op = string.Empty;
            rhs = 0;

            if (string.IsNullOrWhiteSpace(filter))
                return false;

            var s = filter.Trim();

            // order matters
            string[] ops = new[] { ">=", "<=", "!=", "==", ">", "<", "=" };
            foreach (var candidate in ops)
            {
                if (s.StartsWith(candidate, StringComparison.Ordinal))
                {
                    var numberPart = s.Substring(candidate.Length).Trim();

                    if (double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out rhs) ||
                        double.TryParse(numberPart, NumberStyles.Any, CultureInfo.CurrentCulture, out rhs))
                    {
                        op = candidate;
                        return true;
                    }

                    return false;
                }
            }

            return false;
        }

        private static bool IsBareNumericFilter(string f)
        {
            if (string.IsNullOrWhiteSpace(f))
                return false;

            // If it starts with any of our operator prefixes, it's not "bare".
            if (f.StartsWith(">", StringComparison.Ordinal) || f.StartsWith("<", StringComparison.Ordinal) ||
                f.StartsWith("=", StringComparison.Ordinal) || f.StartsWith("!", StringComparison.Ordinal) ||
                f.StartsWith("^", StringComparison.Ordinal) || f.StartsWith("~", StringComparison.Ordinal))
            {
                return false;
            }

            // Must contain at least one digit.
            bool hasDigit = false;
            foreach (var ch in f)
            {
                if (char.IsDigit(ch)) { hasDigit = true; continue; }
                if (ch == '.' || ch == '-' || ch == '+' || ch == ',' || char.IsWhiteSpace(ch)) continue;
                return false;
            }
            return hasDigit;
        }

        private static bool TryParseDoubleLoose(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Try invariant first; then current culture.
            return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ||
                   double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out v);
        }




        private void ApplyFilters(Dictionary<string, TextBox> filters, List<string> columns, StackPanel container, bool enableScoreColors = false)
        {
            container.Children.Clear();

            var filtered = _filteredResults.Where(row =>
            {
                foreach (var col in columns)
                {
                    if (filters.TryGetValue(col, out var filterBox))
                    {
                        var filterText = filterBox.Text?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(filterText))
                        {
                            if (!row.TryGetValue(col, out var valueObj) || valueObj == null)
                                return false;

                            if (!MatchesFilter(valueObj.ToString() ?? string.Empty, filterText))
                                return false;
                        }
                    }
                }
                return true;
            }).ToList();

            // Sync existing selection before rebuilding rows.
            try
            {
                foreach (var kvp in _fqdnCheckboxes)
                {
                    if (kvp.Value?.IsChecked == true)
                        _selectedResultFqdns.Add(kvp.Key);
                    else
                        _selectedResultFqdns.Remove(kvp.Key);
                }
            }
            catch { }

            _fqdnCheckboxes.Clear();
            var fqdnSet = new HashSet<string>();

            foreach (var row in filtered)
            {
                // Use the same computed column widths as the header so everything stays aligned.
                var columnDefs = GenerateGridColumns(columns, GridWidthContext.Results);
                var rowGrid = new Grid
                {
                    ColumnDefinitions = columnDefs,
                    Margin = new Thickness(0, 1, 0, 1)
                };

                CheckBox checkbox = null;
                if (row.TryGetValue("Fqdn", out var fqdn) && !string.IsNullOrWhiteSpace(fqdn))
                {
                    if (!fqdnSet.Contains(fqdn))
                    {
                        checkbox = new CheckBox
                        {
                            IsChecked = _selectedResultFqdns.Contains(fqdn),
                            Tag = fqdn,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            // Column 0 is a fixed 30px checkbox/spacer column.
                            // Any horizontal margin here will spill into column 1 and visually misalign rows.
                            Margin = new Thickness(0)
                        };
                        _fqdnCheckboxes[fqdn] = checkbox;
                        fqdnSet.Add(fqdn);

                        checkbox.Checked += (_, __) =>
                        {
                            try { _selectedResultFqdns.Add(fqdn); } catch { }
                        };
                        checkbox.Unchecked += (_, __) =>
                        {
                            try { _selectedResultFqdns.Remove(fqdn); } catch { }
                        };
                    }
                }

                if (checkbox != null)
                {
                    Grid.SetColumn(checkbox, 0);
                    rowGrid.Children.Add(checkbox);
                }
                else
                {
                    var spacer = new Border { Width = 30 }; // aligns to checkbox width
                    Grid.SetColumn(spacer, 0);
                    rowGrid.Children.Add(spacer);
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    var value = row.TryGetValue(col, out var val) ? val : "";
                    bool isScore = enableScoreColors && (
                        col.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
                        col.Contains("Performance", StringComparison.OrdinalIgnoreCase) ||
                        col.Contains("Disk", StringComparison.OrdinalIgnoreCase) ||
                        col.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                    );

                    bool isFqdn = string.Equals(col, "Fqdn", StringComparison.OrdinalIgnoreCase) || i == 0;

                    if (isFqdn && !string.IsNullOrWhiteSpace(value))
                    {
                        var text = new TextBlock
                        {
                            Text = value,
                            TextDecorations = TextDecorations.Underline,
                            Foreground = Brushes.Blue,
                            TextWrapping = TextWrapping.NoWrap,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center
                        };


                        var button = new Button
                        {
                            Content = text,
                            Padding = new Thickness(0),
                            Background = Brushes.Transparent,
                            BorderBrush = Brushes.Transparent,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            MinWidth = 100,
                            Margin = new Thickness(0),
                            Cursor = new Cursor(StandardCursorType.Hand)
                        };

                        button.Click += (_, __) => ShowDevicePanel(value);
                        // Double-click the FQDN cell to auto-fill the corresponding filter.
                        button.DoubleTapped += (_, __) =>
                        {
                            if (filters.TryGetValue(col, out var fb))
                            {
                                fb.Text = value;
                                ApplyFilters(filters, columns, container, enableScoreColors);
                            }
                        };
                        ToolTip.SetTip(button, value);

                        var cellBorder = new Border
                        {
                            Padding = new Thickness(4, 2, 4, 2),
                            Margin = new Thickness(GridColumnGap / 2, 0, GridColumnGap / 2, 0),
                            ClipToBounds = true,
                            Child = button
                        };

                        Grid.SetColumn(cellBorder, i + 1);
                        rowGrid.Children.Add(cellBorder);
                    }


                    else
                    {
                        var cellText = new TextBlock
                        {
                            Text = value,
                            TextWrapping = TextWrapping.NoWrap,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextAlignment = isScore ? TextAlignment.Center : TextAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };

                        var cellBorder = new Border
                        {
                            Padding = new Thickness(4, 2, 4, 2),
                            Margin = new Thickness(GridColumnGap / 2, 0, GridColumnGap / 2, 0),
                            ClipToBounds = true,
                            Child = cellText
                        };

                        // Ensure the entire cell is hit-testable (double-click should work even when clicking the
                        // colored bar/empty background area).
                        if (cellBorder.Background == null)
                            cellBorder.Background = Brushes.Transparent;

                        cellBorder.DoubleTapped += (_, __) =>
                        {
                            if (filters.TryGetValue(col, out var fb))
                            {
                                fb.Text = value;
                                ApplyFilters(filters, columns, container, enableScoreColors);
                            }
                        };

                        if (enableScoreColors)
                        {
                            cellBorder.Background = GetScoreColor(col, value);
                            cellText.Foreground = Brushes.White;
                        }

                        ToolTip.SetTip(cellBorder, value);
                        Grid.SetColumn(cellBorder, i + 1); // +1 to skip checkbox column
                        rowGrid.Children.Add(cellBorder);
                    }

                }


                var rowBorder = new Border
                {
                    Padding = new Thickness(4, 0, 4, 0),
                    Child = rowGrid
                };

                container.Children.Add(rowBorder);
            }

            // LogToUI($"✅ Showing {filtered.Count} filtered rows.\n");
        }



        private IBrush GetScoreColor(string col, string value)
        {
            if (int.TryParse(value, out int score))
            {
                if (score >= 90) return Brushes.ForestGreen;
                if (score >= 75) return Brushes.Orange;
                if (score > 0) return Brushes.DarkRed;
            }
            return Brushes.Transparent;
        }


        private async void ExportResultsToFile()
        {
            if (_filteredResults == null || _filteredResults.Count == 0)
            {
                LogToUI("⚠️ No results to export.\n");
                return;
            }

            string selectedFormat = GetConfiguredExportFormatOrDefault("csv");
            var dialog = new SaveFileDialog
            {
                Title = "Export Results",
                InitialFileName = $"results.{selectedFormat}",
                DefaultExtension = selectedFormat
            };

            var filePath = await dialog.ShowAsync(this);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (!filePath.EndsWith($".{selectedFormat}", StringComparison.OrdinalIgnoreCase))
                filePath += $".{selectedFormat}";

            try
            {
                if (selectedFormat == "xlsx")
                {
                    await ExportToXlsxAsync(filePath);
                }
                else
                {
                    char separator = selectedFormat == "tsv" ? '\t' : ',';
                    var headers = _filteredResults.SelectMany(d => d.Keys).Distinct().ToList();
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(separator, headers));

                    foreach (var row in _filteredResults)
                    {
                        var line = headers.Select(h => row.ContainsKey(h) ? row[h] : "").ToArray();
                        sb.AppendLine(string.Join(separator, line));
                    }

                    await File.WriteAllTextAsync(filePath, sb.ToString());
                }

                LogToUI($"✅ Exported to: {filePath}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Export failed: {ex.Message}\n");
            }
        }

        private async Task ExportAllResultsToFileAsync()
        {
            if (_lastInstructionId <= 0)
            {
                LogToUI("⚠️ No instruction response selected to export.\n");
                return;
            }

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("⚠️ No platform selected.\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("⚠️ Not authenticated.\n");
                return;
            }

            string selectedFormat = GetConfiguredExportFormatOrDefault("csv");
            var dialog = new SaveFileDialog
            {
                Title = "Export All Results (Paged)",
                InitialFileName = $"results_{_lastInstructionId}.{selectedFormat}",
                DefaultExtension = selectedFormat
            };

            var filePath = await dialog.ShowAsync(this);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (!filePath.EndsWith($".{selectedFormat}", StringComparison.OrdinalIgnoreCase))
                filePath += $".{selectedFormat}";

            try
            {
                LogToUI($"📦 Exporting ALL results for response {_lastInstructionId} (paged)...\n");

                // Uses the streaming/paging export helper (pulls all pages from the API).
                await DexInstructionRunner.Helpers.ExportHelper.ExportInstructionResultsWithProgressAsync(
                    platformUrl: _selectedPlatform.Url,
                    token: _token,
                    consumerName: _consumerName,
                    responseId: _lastInstructionId,
                    filePath: filePath,
                    format: selectedFormat,
                    logger: null,
                    pageSize: 2000);

                LogToUI($"✅ Exported ALL results to: {filePath}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Export ALL failed: {ex.Message}\n");
            }
        }


        private async Task ExportToXlsxAsync(string filePath)
        {
            try
            {
                var workbook = new ClosedXML.Excel.XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Results");

                var headers = _filteredResults.SelectMany(d => d.Keys).Distinct().ToList();

                for (int i = 0; i < headers.Count; i++)
                    worksheet.Cell(1, i + 1).Value = headers[i];

                for (int r = 0; r < _filteredResults.Count; r++)
                {
                    var row = _filteredResults[r];
                    for (int c = 0; c < headers.Count; c++)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = row.ContainsKey(headers[c]) ? row[headers[c]] : "";
                    }
                }

                workbook.SaveAs(filePath);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ XLSX export failed: {ex.Message}\n");
            }

            await Task.CompletedTask;
        }
        private async Task LoadHistoryItemResultsAsync(InstructionHistoryItem selected)
        {
            // ⏩ Switch tab immediately for faster feedback
            var tabControl = this.FindControl<TabControl>("MainTabControl");
            var resultsTab = tabControl?.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header?.ToString() == "Results");
            if (resultsTab != null)
                tabControl.SelectedItem = resultsTab;

            if (!int.TryParse(selected.Id, out int responseId))
            {
                LogToUI($"❌ Invalid response ID: {selected.Id}\n");
                return;
            }

            int definitionId = selected.DefinitionId;
            _lastInstructionId = responseId;

            LogToUI($"📥 Fetching results for Instruction ID (response): {responseId} (definition: {definitionId})\n");

            _lastInstructionDefinition = _instructionDefinitions.FirstOrDefault(i => i.Id == definitionId);
            if (_lastInstructionDefinition == null)
            {
                LogToUI($"⚠️ No local definition found for ID {definitionId}, trying to fetch from server...\n");
                _lastInstructionDefinition = await FetchInstructionDefinitionByIdAsync(definitionId);
            }

            if (_lastInstructionDefinition != null)
            {
                UpdateViewToggles(_lastInstructionDefinition);
                LogToUI($"✅ Found definition: {_lastInstructionDefinition.Name}\n");

                this.FindControl<TextBlock>("InstructionDescription").Text = _lastInstructionDefinition.Description;

                var paramPanel = TryFindControl<StackPanel>("ParametersPanel");
                RenderParameters(paramPanel, _lastInstructionDefinition.Parameters ?? new());
            }
            else
            {
                LogToUI($"❌ Still no definition available for ID: {definitionId}. Cannot populate description or parameters.\n");
            }
            ClearAllResultPanels();

            await RefreshResultsHeaderFromInstructionAsync(responseId);
            await LoadInstructionResultsAsync(responseId);
            RenderResultsList(_filteredResults);
            await ShowInstructionProgressAsync(_lastInstructionId, true); // 'true' here means enable logging
            UpdateView("Raw");
        }


        private async void HistoryDatePicker_SelectedDateChanged(object sender, Avalonia.Controls.DatePickerSelectedValueChangedEventArgs e)
        {
            // Call the method to reload history based on the new selected date
            await LoadInstructionHistoryAsync();
        }


        private async Task LoadInstructionHistoryAsync()
        {
            try
            {
                // Ensure _selectedPlatform and _token are valid before proceeding
                if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
                {
                    LogToUI("❌ Cannot fetch principal: Platform not selected.\n");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_token))
                {
                    LogToUI("❌ Cannot fetch principal: Token is not available.\n");
                    return;
                }

                // If the principal name is not set, fetch it
                if (string.IsNullOrEmpty(_principalName))
                {
                    // Fetch the principal name based on the current platform and token
                    await GetPrincipalNameAsync(_selectedPlatform.Url, _token);
                }

                string createdBy = _principalName;

                // Validate the selected platform and token before continuing
                if (_selectedPlatform == null)
                {
                    LogToUI("❌ Platform not selected.\n");
                    return;
                }

                if (string.IsNullOrEmpty(_token))
                {
                    LogToUI("❌ Token is not available.\n");
                    return;
                }

                // Date filters
                var selectedDate = _historyDatePicker?.SelectedDate ?? DateTime.UtcNow;
                var selectedDateAtMidnightLocal = selectedDate.Date;

                // Specify it's UTC midnight (not local)
                var fromDateUtc = DateTime.SpecifyKind(selectedDateAtMidnightLocal, DateTimeKind.Utc);
                var toDateUtc = DateTime.SpecifyKind(selectedDateAtMidnightLocal.AddDays(1), DateTimeKind.Utc);

                var fromUtcString = fromDateUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var toUtcString = toDateUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");


                string postUrl = $"https://{_selectedPlatform.Url}/consumer/Instructions/Search";
                LogToUI($"📅 Fetching instructions since {fromUtcString}...\n");

                try
                {
                    _isLegacyVersion = await VersionService.IsLegacyVersionAsync(_selectedPlatform.Url, _token);
                }
                catch (Exception ex)
                {
                    LogToUI($"❌ Error checking legacy version: {ex.Message}\n");
                    return;
                }

                LogToUI($"🧭 Legacy mode = {_isLegacyVersion}\n");

                // Legacy API: Use simple sorting and filters
                object[] sortBlock = _isLegacyVersion
                    ? new object[] { new { label = "Newest first", Column = "CreatedTime", Direction = "DESC" } }  // Legacy sort format
                    : new object[] { new { column = "CreatedTime", direction = "DESC" } };  // Modern sort format


                var filterPayload = new
                {
                    Filter = new
                    {
                        Operator = "AND",
                        Operands = new object[]
                        {
               // Nested filter for CreatedBy and CreatedTime (date interval)


               new
               {
                   Operator = "AND",
                   Operands = new object[]
                   {
                       new { Attribute = "CreatedBy", Operator = "like", Value = $"%{createdBy}%" },
                       new { Attribute = "CreatedTime", Operator = ">=", Value = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"), Type = "date" }

                   }
               },
               new { Attribute = "ConsumerId", Operator = "==", Value = 1 }
                        }
                    },
                    Sort = sortBlock,
                    Start = 1,
                    PageSize = 50
                };

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var payloadJson = JsonConvert.SerializeObject(filterPayload);
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                // Log the full payload being sent to the API for debugging purposes
                LogToUI($"💬 Payload being sent:\n{payloadJson}\n");
                LogToUI($"💬 Endpoint:\n{postUrl}\n");

                var json = await ApiLogger.LogApiCallAsync(
                    label: "InstructionHistory",
                    endpoint: "Consumer/Instructions/Search",
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync(postUrl, content);
                        if (!response.IsSuccessStatusCode)
                        {
                            LogToUI($"❌ History fetch failed: {response.StatusCode}\n");
                        }

                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: payloadJson
                );

                if (string.IsNullOrWhiteSpace(json))
                {
                    LogToUI("❌ No instructions returned from history.\n");
                    return;
                }

                // Log the raw response from the API to see what's coming back
                //  LogToUI($"💬 API Response:\n{json}\n");

                var parsed = JObject.Parse(json);
                JArray historyArray = parsed["Items"] as JArray;

                if (historyArray == null || historyArray.Count == 0)
                {
                    LogToUI("⚠️ No instructions found in history.\n");
                    return;
                }

                var historyList = historyArray.Select(item => new InstructionHistoryItem
                {
                    Id = item["Id"]?.ToString(),
                    Name = item["Name"]?.ToString(),
                    Created = item["CreatedTimestampUtc"]?.ToString(),
                    Status = GetStatusText(item["Status"]?.ToString() ?? ""),
                    DefinitionId = item["InstructionDefinitionId"]?.ToObject<int?>() ?? -1
                }).ToList();

                if (this.FindControl<ComboBox>("StatusFilterComboBox")?.SelectedItem is ComboBoxItem selectedStatus)
                {
                    var statusFilter = selectedStatus.Content?.ToString();
                    if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All Statuses")
                    {
                        historyList = historyList
                            .Where(item => item.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                }

                _historyListBox.ItemsSource = historyList;
                LogToUI($"✅ Loaded {historyList.Count} instructions from history.\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error loading instruction history: {ex.Message}\n");
            }
        }


        private void UpdateInstructionDetailStatus(string status)
        {
            var statusTextBlock = this.FindControl<TextBlock>("InstructionStatusTextBlock");

            if (statusTextBlock != null)
            {
                statusTextBlock.Text = $"Status: {status}";
            }
        }





        private string GetStatusText(string statusCode)
        {
            return statusCode switch
            {
                "0" => "Created",
                "1" => "In Approval",
                "2" => "Rejected",
                "3" => "Approved",
                "4" => "Sent",
                "5" => "In Progress",
                "6" => "Completed",
                "7" => "Expired",
                "8" => "Cancelling Responses still Available",
                "9" => "Cancelled",
                "10" => "Failed",
                "11" => "Suspended",
                "12" => "Awaiting Authentication",
                "13" => "Evaluating Condition",
                _ => $"Unknown ({statusCode})"
            };
        }

        private const int MaxDisplayRows = 2500;

        private void ApplyLimitedFilters(Dictionary<string, TextBox> filters, List<string> columns, StackPanel container)
        {
            container.Children.Clear();
            var filtered = _filteredResults.Where(row =>
            {
                foreach (var col in columns)
                {
                    if (filters.TryGetValue(col, out var filterBox))
                    {
                        var filterText = filterBox.Text?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(filterText) &&
                            (!row.TryGetValue(col, out var value) || !value.Contains(filterText, StringComparison.OrdinalIgnoreCase)))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }).ToList();

            _fqdnCheckboxes.Clear();
            var fqdnSet = new HashSet<string>();

            var limited = filtered.Take(MaxDisplayRows).ToList();
            if (filtered.Count > MaxDisplayRows)
            {
                LogToUI($"⚠️ Showing first {MaxDisplayRows} of {filtered.Count} result rows.\n");
            }

            foreach (var row in limited)
            {
                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

                if (row.TryGetValue("Fqdn", out var fqdn) && !string.IsNullOrWhiteSpace(fqdn))
                {
                    if (!fqdnSet.Contains(fqdn))
                    {
                        var cb = new CheckBox
                        {
                            IsChecked = _selectedResultFqdns.Contains(fqdn),
                            Width = 20,
                            Tag = fqdn
                        };
                        _fqdnCheckboxes[fqdn] = cb;
                        fqdnSet.Add(fqdn);
                        itemPanel.Children.Add(cb);
                    }
                    else
                    {
                        itemPanel.Children.Add(new TextBlock { Width = 20 });
                    }
                }
                else
                {
                    itemPanel.Children.Add(new TextBlock { Width = 20 });
                }

                foreach (var col in columns)
                {
                    var value = row.TryGetValue(col, out var val) ? val : "";
                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = value,
                        Width = 160
                    });
                }

                container.Children.Add(itemPanel);
            }

            LogToUI($"✅ Rendered {limited.Count} limited result rows.\n");
        }


        private static async Task<string> PostHttpRequest(HttpClient client, string url, string payload)
        {
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            return await response.Content.ReadAsStringAsync();
        }

        private static void OpenBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Process.Start("xdg-open", url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening browser: {ex.Message}");
            }
        }

        private static string ToBase64Url(byte[] input)
        {
            return Convert.ToBase64String(input).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private async Task<int?> RerunInstructionAsync()
        {
            // NOTE: /consumer/Instructions/{id}/rerun expects the *execution/run id*.
            int rerunRunId = 0;
            int definitionIdForRefresh = 0;

            if (_historyListBox == null)
                _historyListBox = TryFindControl<ListBox>("HistoryListBox");

            if (_historyListBox?.SelectedItem is InstructionHistoryItem selected)
            {
                if (!string.IsNullOrWhiteSpace(selected.Id) &&
                    int.TryParse(selected.Id, out var parsedRunId) &&
                    parsedRunId > 0)
                {
                    rerunRunId = parsedRunId;
                }

                if (selected.DefinitionId > 0)
                {
                    definitionIdForRefresh = selected.DefinitionId;
                }
            }

            // Do NOT fall back to definition id here; rerun endpoint needs run/execution id.
            if (rerunRunId <= 0)
            {
                LogToUI("❌ No run/execution selected to rerun. Please select a History item first.", "Warning");
                return null;
            }

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("⚠️ No platform selected.", "Warning");
                return null;
            }

            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("⚠️ Not authenticated.", "Warning");
                return null;
            }

            var url = $"https://{_selectedPlatform.Url}/consumer/Instructions/{rerunRunId}/rerun";
            LogToUI($"🔁 Sending rerun request: POST {url}");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                bool ok = false;
                string? bodyCaptured = null;

                _ = await ApiLogger.LogApiCallAsync(
                    label: "InstructionRerun",
                    endpoint: $"Consumer/Instructions/{rerunRunId}/rerun",
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync(url, null);
                        ok = response.IsSuccessStatusCode;
                        var body = await response.Content.ReadAsStringAsync();
                        bodyCaptured = body;

                        if (!ok)
                            LogToUI($"❌ Rerun failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}", "Error");

                        return body;
                    },
                    payloadJson: "(none)"
                );

                if (!ok)
                    return null;

                // Parse new run id from response
                var newRunId = TryExtractRunIdFromRerunResponse(bodyCaptured);
                var effectiveRunId = newRunId > 0 ? newRunId : rerunRunId;

                if (newRunId <= 0)
                    LogToUI("✅ Instruction rerun successfully (no new run id returned).");
                else
                    LogToUI($"✅ Instruction rerun successfully. New Run Id: {newRunId}");

                if (definitionIdForRefresh > 0)
                {
                    _lastInstructionDefinition =
                        _instructionDefinitions.FirstOrDefault(i => i.Id == definitionIdForRefresh) ?? _lastInstructionDefinition;
                }

                await LoadInstructionHistoryAsync();

                // If we have a run id, select it AND load its results via the same path as a normal history click.
                // This ensures the Results header (instruction name / coverage / filter) updates consistently.
                if (_historyListBox != null)
                {
                    InstructionHistoryItem? match = null;

                    try
                    {
                        match = _historyListBox.Items
                            ?.OfType<InstructionHistoryItem>()
                            ?.FirstOrDefault(h => string.Equals(h.Id, effectiveRunId.ToString(), StringComparison.OrdinalIgnoreCase));
                    }
                    catch { }

                    if (match != null)
                    {
                        try { _historyListBox.SelectedItem = match; } catch { }

                        try
                        {
                            // Keep Results header context (instruction name/coverage/filter) in sync with the selected run.
                            await LoadHistoryItemResultsAsync(match);
                        }
                        catch (Exception ex)
                        {
                            LogToUI($"⚠️ Rerun: failed to load history item results for {effectiveRunId}: {ex.Message}", "Warning");
                        }
                    }
                }

                UpdateView("Raw");

                var tabControl = this.FindControl<TabControl>("MainTabControl");
                if (tabControl != null)
                    tabControl.SelectedIndex = 2;

                return effectiveRunId;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Exception during rerun: {ex.Message}", "Error");
                return null;
            }
        }

        private static int TryExtractRunIdFromRerunResponse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;

            var s = body.Trim();

            // Sometimes API returns a plain number
            if (int.TryParse(s, out var plainId) && plainId > 0)
                return plainId;

            // Sometimes API returns { "Id": 123 } or similar
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(s);
                var root = doc.RootElement;

                if (root.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    if (root.TryGetInt32(out var n) && n > 0)
                        return n;
                }

                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // Common property names across 1E APIs / variants
                    string[] keys =
                    {
                "Id", "id",
                "RunId", "runId",
                "ExecutionId", "executionId",
                "InstructionId", "instructionId",
                "NewId", "newId",
                "NewRunId", "newRunId",
                "NewExecutionId", "newExecutionId"
            };

                    foreach (var k in keys)
                    {
                        if (root.TryGetProperty(k, out var p))
                        {
                            if (p.ValueKind == System.Text.Json.JsonValueKind.Number &&
                                p.TryGetInt32(out var id) && id > 0)
                                return id;

                            if (p.ValueKind == System.Text.Json.JsonValueKind.String &&
                                int.TryParse(p.GetString(), out var sid) && sid > 0)
                                return sid;
                        }
                    }
                }
            }
            catch
            {
                // ignore parse errors
            }

            return 0;
        }


        private async Task CancelInstructionAsync()
        {
            if (_lastInstructionId <= 0)
            {
                LogToUI("❌ No instruction to cancel.\n");
                return;
            }

            var url = $"https://{_selectedPlatform?.Url}/consumer/Instructions/{_lastInstructionId}/Cancel/true";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            var json = await ApiLogger.LogApiCallAsync(
                label: "InstructionCancel",
                endpoint: $"Consumer/Instructions/{_lastInstructionId}/Cancel/true",
                apiCall: async () =>
                {
                    var response = await client.PostAsync(url, null);
                    if (!response.IsSuccessStatusCode)
                    {
                        LogToUI($"❌ Cancel failed: {response.StatusCode}\n");
                    }
                    return await response.Content.ReadAsStringAsync();
                },
                payloadJson: "(none)"
            );

            LogToUI($"✅ Instruction {_lastInstructionId} canceled.\n");
            await LoadInstructionHistoryAsync();
        }


        private void ResetFiltersButton_Click(object? sender, RoutedEventArgs e)
        {
            var resultsPanel = this.FindControl<StackPanel>("ResultsPanel");
            var filterBoxes = resultsPanel.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(tb => tb.Watermark?.StartsWith("Filter") == true);

            foreach (var box in filterBoxes)
                box.Text = string.Empty;

            LogToUI("🔄 Filters reset.\n");
        }

        private async Task GetPrincipalNameAsync(string platformUrl, string token)
        {
            try
            {
                // Ensure platformUrl and token are provided
                if (string.IsNullOrWhiteSpace(platformUrl))
                {
                    LogToUI("❌ Cannot fetch principal: Platform URL is not provided.\n");
                    return;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    LogToUI("❌ Cannot fetch principal: Token is not available.\n");
                    return;
                }

                // Log platform URL and token for debugging purposes
                LogToUI($"🌐 Fetching principal for platform: {platformUrl}\n");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);

                // Make the request to the platform's "whoami" endpoint to fetch the principal information
                var response = await client.GetAsync($"https://{platformUrl}/Consumer/PrincipalSearch/whoami");
                var json = await response.Content.ReadAsStringAsync();
                dynamic whoami = JsonConvert.DeserializeObject(json);

                // Set the principal name
                _principalName = whoami?.PrincipalName;

                // Log the fetched principal name
                LogToUI($"👤 Principal: {_principalName}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching principal: {ex.Message}\n");
            }
        }



        private async Task UpdateView(string mode)
        {
            // If the currently selected view isn't available for this instruction, fall back safely.
            if (mode == "Chart")
            {
                var chartRadio = this.FindControl<RadioButton>("ChartViewRadioButton");
                if (chartRadio == null || chartRadio.IsVisible == false)
                {
                    mode = "Raw";
                }
            }
            else if (mode == "Aggregated")
            {
                var aggRadio = this.FindControl<RadioButton>("AggregatedViewRadioButton");
                if (aggRadio == null || aggRadio.IsVisible == false)
                {
                    mode = "Raw";
                }
            }

            if (mode != _currentViewMode)
                _currentViewMode = mode;


            switch (mode)
            {
                case "Raw":
                    if (_filteredResults != null && _filteredResults.Any())
                    {
                        Console.WriteLine($"RenderResultsList called with {_filteredResults.Count} rows");
                        RenderResultsList(_filteredResults);
                    }
                    else
                    {
                        LogToUI("⚠️ No filtered results available for Raw view.\n");
                    }
                    break;

                case "Aggregated":
                    await LoadAggregatedResultsAsync(_lastInstructionId);
                    break;
                case "Chart":
                    await LoadChartResultsAsync(_lastInstructionId);
                    break;
            }
        }


        private async Task LoadChartResultsAsync(int instructionId)
        {
            var loadingPanel = this.FindControl<StackPanel>("LoadingStatusPanel");
            if (loadingPanel != null)
                loadingPanel.IsVisible = true;

            try
            {
                var url = $"https://{_selectedPlatform?.Url}/consumer/Responses/Processed/{instructionId}";
                LogToUI($"📊 Chart Request:\nGET {url}\n");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var json = await ApiLogger.LogApiCallAsync(
                     label: "Chart Results",
                     endpoint: $"consumer/Responses/Processed/{instructionId}",
                     apiCall: async () =>
                     {
                         var response = await client.GetAsync(url);
                         var resultContent = await response.Content.ReadAsStringAsync();

                         if (!response.IsSuccessStatusCode)
                         {
                             LogToUI($"❌ Error loading chart results: {response.StatusCode}\n{resultContent}\n");
                             return null;
                         }

                         return resultContent;
                     },
                     payloadJson: "" // GET
                 );

                if (string.IsNullOrWhiteSpace(json))
                    return;

                var parsed = JObject.Parse(json);
                var responseTemplateConfigs = _lastInstructionDefinition?.ResponseTemplateConfiguration?.TemplateConfigurations;

                if (responseTemplateConfigs != null && responseTemplateConfigs.Count > 0)
                {
                    var chartPanel = this.FindControl<StackPanel>("ResultsPanel");
                    if (chartPanel == null)
                    {
                        if (loadingPanel != null)
                            loadingPanel.IsVisible = false;
                        return;
                    }

                    // Preserve current chart selection across refreshes (ChartRenderer rebuilds UI each time)
                    try
                    {
                        var existingCombo = FindFirstComboBoxInTree(chartPanel);
                        if (existingCombo != null)
                        {
                            _resultsChartLastSelectedIndex = existingCombo.SelectedIndex >= 0 ? existingCombo.SelectedIndex : null;

                            if (existingCombo.SelectedItem is ComboBoxItem cbi && cbi.Content is string s1)
                                _resultsChartLastSelectedLabel = s1;
                            else if (existingCombo.SelectedItem is string s2)
                                _resultsChartLastSelectedLabel = s2;
                            else if (existingCombo.SelectedItem != null)
                                _resultsChartLastSelectedLabel = existingCombo.SelectedItem.ToString();
                        }
                    }
                    catch
                    {
                        // best-effort
                    }

                    chartPanel.Children.Clear();

                    var themeVariant = Application.Current?.ActualThemeVariant;
                    bool isDark = themeVariant == ThemeVariant.Dark;

                    ChartRenderer.RenderChartDropdown(parsed, JArray.FromObject(responseTemplateConfigs), chartPanel, isDark);

                    // Restore selection and keep tracking user changes.
                    try
                    {
                        var newCombo = FindFirstComboBoxInTree(chartPanel);

                        if (newCombo != null)
                        {
                            newCombo.SelectionChanged -= ResultsChartDropdown_SelectionChanged;
                            newCombo.SelectionChanged += ResultsChartDropdown_SelectionChanged;

                            if (!string.IsNullOrWhiteSpace(_resultsChartLastSelectedLabel))
                            {
                                for (int i = 0; i < newCombo.ItemCount; i++)
                                {
                                    var item = newCombo.Items[i];
                                    string? label = null;

                                    if (item is ComboBoxItem itemCbi && itemCbi.Content is string s2)
                                        label = s2;
                                    else if (item != null)
                                        label = item.ToString();

                                    if (string.Equals(label, _resultsChartLastSelectedLabel, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newCombo.SelectedIndex = i;
                                        break;
                                    }
                                }
                            }
                            else if (_resultsChartLastSelectedIndex.HasValue)
                            {
                                var idxToSet = _resultsChartLastSelectedIndex.Value;
                                if (idxToSet >= 0 && idxToSet < newCombo.ItemCount)
                                    newCombo.SelectedIndex = idxToSet;
                            }
                        }
                    }
                    catch
                    {
                        // best-effort
                    }
                }
                else
                {
                    // Fallback: some instructions return chart payloads (e.g., "mainchart") even when the
                    // definition doesn't expose ResponseTemplateConfiguration reliably. In that case, still
                    // render a basic chart so the user sees something consistently.
                    try
                    {
                        var chartPanel = this.FindControl<StackPanel>("ResultsPanel");
                        if (chartPanel != null)
                        {
                            var main = parsed["mainchart"] as JArray;
                            var first = main?.FirstOrDefault() as JObject;
                            var items = first?["Items"] as JArray;

                            if (items != null && items.Count > 0)
                            {
                                // Preserve selection state variables so subsequent refreshes don't break.
                                chartPanel.Children.Clear();

                                var themeVariant = Application.Current?.ActualThemeVariant;
                                bool isDark = themeVariant == ThemeVariant.Dark;

                                var title = first?["Name"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    chartPanel.Children.Add(new TextBlock
                                    {
                                        Text = title,
                                        FontSize = 14,
                                        FontWeight = FontWeight.Bold,
                                        Margin = new Thickness(0, 6, 0, 6)
                                    });
                                }

                                var holder = new StackPanel();
                                ChartRenderer.RenderChartResults(items, chartType: "Bar", resultsPanel: holder, xField: "Version", yField: "Count", isDark: isDark);
                                chartPanel.Children.Add(holder);
                            }
                            else
                            {
                                LogToUI($"⚠️ No chart configuration found in instruction definition.\n");
                            }
                        }
                        else
                        {
                            LogToUI($"⚠️ No chart configuration found in instruction definition.\n");
                        }
                    }
                    catch
                    {
                        LogToUI($"⚠️ No chart configuration found in instruction definition.\n");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Exception while loading chart results: {ex.Message}\n");
            }
            finally
            {
#if DEBUG
                LogToUI($"✅ Done loading chart results for ID {instructionId}.\n");
#endif
                if (loadingPanel != null)
                    loadingPanel.IsVisible = false;
            }
        }

        private static ComboBox? FindFirstComboBoxInTree(Control? root)
        {
            if (root == null)
                return null;

            if (root is ComboBox cb)
                return cb;

            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Control c)
                    {
                        var found = FindFirstComboBoxInTree(c);
                        if (found != null)
                            return found;
                    }
                }

                return null;
            }

            if (root is ContentControl cc)
                return FindFirstComboBoxInTree(cc.Content as Control);

            if (root is Decorator dec)
                return FindFirstComboBoxInTree(dec.Child);

            return null;
        }






        private async Task LoadInstructions()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            var json = await ApiLogger.LogApiCallAsync(
                label: "InstructionDefinitions",
                endpoint: "Consumer/InstructionDefinitions",
                apiCall: async () =>
                {
                    var response = await client.GetAsync($"https://{_selectedPlatform?.Url}/Consumer/InstructionDefinitions");
                    return await response.Content.ReadAsStringAsync();
                },
                payloadJson: ""
            );

            JArray array = JArray.Parse(json);
            _instructionDefinitions.Clear();
            _instructionMap.Clear();

            foreach (var item in array)
            {
                string name = item["Name"]?.ToString() ?? "(Unnamed)";
                int id = item["Id"]?.Value<int>() ?? -1;
                string description = item["Description"]?.ToString() ?? "";

                var parameters = new List<Parameter>();
                if (item["Parameters"] is JArray rawParams)
                {
                    foreach (var p in rawParams)
                    {
                        var param = new Parameter
                        {
                            Name = p["Name"]?.ToString(),
                            DefaultValue = p["DefaultValue"]?.ToString(),
                            Description = p["Description"]?.ToString(),
                            ControlType = p["ControlType"]?.ToString(),
                            Placeholder = p["Placeholder"]?.ToString(),
                            Pattern = p["Pattern"]?.ToString(),
                            DataType = p["DataType"]?.ToString(),
                            Source = p["Source"]?.ToString(),
                            HintText = p["HintText"]?.ToString(),
                            ControlMetadata = p["ControlMetadata"]?.ToString(),
                            Value = p["Value"]?.ToString(),
                            Validation = p["Validation"]?.ToObject<ValidationData>()
                        };

                        parameters.Add(param);
                    }
                }

                var definition = new InstructionDefinition
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    Parameters = parameters,
                    Aggregation = item["Aggregation"]?.ToObject<AggregationConfig>(),
                    ResponseTemplateConfiguration = item["ResponseTemplateConfiguration"]?.ToObject<ResponseTemplateConfiguration>(),
                    InstructionTtlMinutes = item["InstructionTtlMinutes"]?.Value<int>() ?? 60,
                    ResponseTtlMinutes = item["ResponseTtlMinutes"]?.Value<int>() ?? 120,
                    MinimumInstructionTtlMinutes = item["MinimumInstructionTtlMinutes"]?.Value<int>() ?? 10,
                    MaximumInstructionTtlMinutes = item["MaximumInstructionTtlMinutes"]?.Value<int>() ?? 10080,
                    MinimumResponseTtlMinutes = item["MinimumResponseTtlMinutes"]?.Value<int>() ?? 10,
                    MaximumResponseTtlMinutes = item["MaximumResponseTtlMinutes"]?.Value<int>() ?? 10080,
                };

                _instructionDefinitions.Add(definition);
                if (id >= 0) _instructionMap[name] = id;
            }

            _instructionListBox.ItemsSource = _instructionMap.Keys.ToList();

            UpdateFqdnInstructionList(_fqdnInstructionSearchBox?.Text);
        }
        private async Task<InstructionDefinition?> FetchInstructionDefinitionByIdAsync(int instructionId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                // NOTE: definition-by-id is under /consumer/InstructionDefinitions/id/{id}
                var json = await ApiLogger.LogApiCallAsync(
                    label: $"InstructionDefinition_{instructionId}",
                    endpoint: $"consumer/InstructionDefinitions/id/{instructionId}",
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync($"https://{_selectedPlatform?.Url}/consumer/InstructionDefinitions/id/{instructionId}");
                        if (!response.IsSuccessStatusCode)
                        {
                            LogToUI($"❌ Failed to fetch definition: {response.StatusCode}\n");
                            return null;
                        }
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: ""
                );

                return json != null ? JsonConvert.DeserializeObject<InstructionDefinition>(json) : null;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error fetching definition: {ex.Message}\n");
                return null;
            }
        }


        private async void InstructionListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_instructionListBox.SelectedItem is string selectedName &&
                _instructionMap.TryGetValue(selectedName, out int id))
            {
                _selectedInstruction = _instructionDefinitions.FirstOrDefault(i => i.Id == id);
                if (_selectedInstruction == null) return;

                _currentInstructionSchemaDefinitionId = -1;
                _currentInstructionSchemaColumns.Clear();

                _lastInstructionDefinition = _selectedInstruction;

                // If the user has "Filtered" selected for Run scope, immediately fetch schema and populate the filter UI.
                if (_runScopeFilteredRadio?.IsChecked == true)
                {
                    try
                    {
                        await EnsureSelectedInstructionSchemaLoadedAsync();
                        ApplySchemaColumnsToRunFilterUi();
                    }
                    catch { }
                }

                this.FindControl<TextBlock>("InstructionDescription").Text = _selectedInstruction.Description ?? "No description";

                _instructionTtlBox.Text = _selectedInstruction.InstructionTtlMinutes.ToString();
                _responseTtlBox.Text = _selectedInstruction.ResponseTtlMinutes.ToString();

                // Initial sync (in case it's visible immediately)
                SetTtlBoxToAccessibleDefault(_instructionTtlBox);
                SetTtlBoxToAccessibleDefault(_responseTtlBox);

                // Delayed sync to override theme flicker after layout
                Dispatcher.UIThread.Post(() =>
                {
                    SetTtlBoxToAccessibleDefault(_instructionTtlBox);
                    SetTtlBoxToAccessibleDefault(_responseTtlBox);
                }, DispatcherPriority.Background);





                var instructionTtlHint = this.FindControl<TextBlock>("InstructionTtlRangeText");
                if (_selectedInstruction != null && instructionTtlHint != null)
                {
                    int minTtl = _selectedInstruction.MinimumInstructionTtlMinutes > 0 ? _selectedInstruction.MinimumInstructionTtlMinutes : 10;
                    int maxTtl = _selectedInstruction.MaximumInstructionTtlMinutes > 0 ? _selectedInstruction.MaximumInstructionTtlMinutes : 10080;

                    instructionTtlHint.Text = $"Allowed: {minTtl}–{maxTtl}";
                }

                var responseTtlHint = this.FindControl<TextBlock>("ResponseTtlRangeText");
                if (_selectedInstruction != null && responseTtlHint != null)
                {
                    int minTtl = _selectedInstruction.MinimumResponseTtlMinutes > 0 ? _selectedInstruction.MinimumResponseTtlMinutes : 10;
                    int maxTtl = _selectedInstruction.MaximumResponseTtlMinutes > 0 ? _selectedInstruction.MaximumResponseTtlMinutes : 10080;

                    responseTtlHint.Text = $"Allowed: {minTtl}–{maxTtl}";
                }

                UpdateViewToggles(_selectedInstruction);
                RenderParameters(this.FindControl<StackPanel>("ParametersPanel"), _selectedInstruction.Parameters);

                SyncInstructionToFqdnTab();

                // ✅ Trigger target preview
                await UpdatePreviewAsync();
            }
        }



        private void UpdateViewToggles(InstructionDefinition def)
        {
            var hasAggregation = def.Aggregation?.Schema?.Any() == true;
            var hasCharts = def.ResponseTemplateConfiguration?.TemplateConfigurations?.Any() == true;

            var rawRadio = this.FindControl<RadioButton>("RawViewRadioButton");
            var aggRadio = this.FindControl<RadioButton>("AggregatedViewRadioButton");
            var chartRadio = this.FindControl<RadioButton>("ChartViewRadioButton");

            aggRadio.IsVisible = hasAggregation;
            chartRadio.IsVisible = hasCharts;

            // If a view is not supported for this instruction, ensure its radio is unchecked.
            // Radios can stay checked even when hidden, which would cause us to keep trying to load a view that doesn't exist.
            _suppressResultsViewRadioChanged = true;
            try
            {
                if (!hasCharts && chartRadio != null)
                    chartRadio.IsChecked = false;
                if (!hasAggregation && aggRadio != null)
                    aggRadio.IsChecked = false;

                // Ensure we always have a valid selection.
                if (rawRadio != null)
                {
                    var aggChecked = aggRadio?.IsChecked == true;
                    var chartChecked = chartRadio?.IsChecked == true;

                    if (!aggChecked && !chartChecked)
                        rawRadio.IsChecked = true;
                }
            }
            finally
            {
                _suppressResultsViewRadioChanged = false;
            }

            // Reset view mode if needed
            if (!hasAggregation && _currentViewMode == "Aggregated")
            {
                _currentViewMode = "Raw";
                rawRadio.IsChecked = true;
            }

            if (!hasCharts && _currentViewMode == "Chart")
            {
                _currentViewMode = "Raw";
                rawRadio.IsChecked = true;
            }

            // 
            // Apply the currently selected (and supported) mode immediately.
            try
            {
                var mode =
                    (chartRadio?.IsChecked == true && chartRadio.IsVisible == true) ? "Chart" :
                    (aggRadio?.IsChecked == true && aggRadio.IsVisible == true) ? "Aggregated" :
                    "Raw";

                // Fire-and-forget UI update; guard inside UpdateView prevents unsupported modes.
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    try { await UpdateView(mode); } catch { }
                });
            }
            catch { }

            // Force layout refresh
            aggRadio.InvalidateVisual();
            chartRadio.InvalidateVisual();
            aggRadio.InvalidateMeasure();
            chartRadio.InvalidateMeasure();
        }




        private async Task LoadAggregatedResultsAsync(int instructionId)
        {
            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url))
            {
                LogToUI("⚠️ No platform selected.\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(_consumerName) || string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("⚠️ Not authenticated.\n");
                return;
            }

            var url = $"https://{_selectedPlatform.Url}/consumer/Responses/{instructionId}/Aggregate";

            // This endpoint expects POST with a JSON body.
            var payloadObj = new JObject
            {
                ["Filter"] = new JObject(),
                ["Start"] = "0;0",
                ["PageSize"] = 20
            };
            var payloadJson = payloadObj.ToString(Newtonsoft.Json.Formatting.None);

            LogToUI($"📊 Aggregated Request:\nPOST {url}\n{payloadJson}\n");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

            var json = await ApiLogger.LogApiCallAsync(
                label: "AggregatedResults",
                endpoint: $"consumer/Responses/{instructionId}/Aggregate",
                apiCall: async () =>
                {
                    using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(url, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        LogToUI($"❌ Aggregated API failed: {response.StatusCode}\n");
                        var errorBody = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(errorBody))
                            LogToUI($"❌ Aggregated error body: {errorBody}\n");
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync();
                },
                payloadJson: payloadJson
            );

            if (string.IsNullOrWhiteSpace(json))
            {
                LogToUI("❌ Aggregated response was empty or failed.\n");
                return;
            }

            var resultsPanel = this.FindControl<StackPanel>("ResultsPanel");
            if (resultsPanel == null)
            {
                LogToUI("❌ ERROR: ResultsPanel is null. Check your x:Name or XAML structure.\n");
                return;
            }

            resultsPanel.Children.Clear();

            try
            {
                var parsed = JObject.Parse(json);

                // Expect: { "Range": ..., "Responses": [ ... ] }
                var responsesArr = parsed["Responses"] as JArray;
                if (responsesArr == null)
                {
                    LogToUI("⚠️ Aggregate response missing 'Responses'. Showing raw JSON.\n");
                    resultsPanel.Children.Add(new TextBlock { Text = json, TextWrapping = TextWrapping.Wrap });
                    return;
                }

                // Build row dictionaries with dynamic columns
                var noisyTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TachyonGuid","Fqdn","Id","ShardId","Status","ExecTime",
            "CreatedTimestampUtc","ResponseTimestampUtc","CreatedTimestamp","ResponseTimestamp",
            "InstructionGuid","RowSourceType","DataSource",
            // fields that may contain the real payload (we handle separately)
            "Blob","Values","Data","Result"
        };

                var rows = new List<Dictionary<string, string>>(responsesArr.Count);
                var colOrder = new List<string>();
                var colSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var tok in responsesArr)
                {
                    if (tok is not JObject rowObj)
                        continue;

                    // Use Blob/Values/Data/Result if possible, otherwise cleaned top-level
                    var dataObj = ExtractBestDataObject(rowObj, noisyTopLevel);
                    if (dataObj == null || !dataObj.Properties().Any())
                        continue;

                    var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var p in dataObj.Properties())
                    {
                        var key = p.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var val = ToCellString(p.Value);
                        rowDict[key] = val;

                        if (colSet.Add(key))
                            colOrder.Add(key);
                    }

                    rows.Add(rowDict);
                }

                if (rows.Count == 0)
                {
                    LogToUI("⚠️ No renderable aggregate rows found. Showing raw JSON.\n");
                    resultsPanel.Children.Add(new TextBlock { Text = json, TextWrapping = TextWrapping.Wrap });
                    return;
                }

                // Optional: show range info (small)
                if (parsed["Range"] != null)
                {
                    resultsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Range: {parsed["Range"]!.ToString(Newtonsoft.Json.Formatting.None)}",
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }

                // Prefer some known columns first if they exist, but keep fully dynamic
                var preferred = new[]
                {
            "OperatingSystem",
            "Agents",
            "TotalCrashDumps",
            "TotalBytesReceived",
            "TotalBytesSent",
            "TotalConnectionsSuccessful",
            "TotalConnectionsFailed",
            "InstructionId",
            "NumberOfDevicesThatSentResponses"
        };

                var ordered = new List<string>();
                foreach (var p in preferred)
                    if (colSet.Contains(p))
                        ordered.Add(p);

                foreach (var c in colOrder)
                    if (!ordered.Contains(c, StringComparer.OrdinalIgnoreCase))
                        ordered.Add(c);

                // Build DataGrid (dynamic columns) - use TemplateColumn + converter (reliable in Avalonia)
                var lookup = new DictLookupConverter();

                var grid = new Avalonia.Controls.DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    GridLinesVisibility = Avalonia.Controls.DataGridGridLinesVisibility.Horizontal,
                    Margin = new Thickness(0, 4, 0, 0),
                    MinHeight = 240
                };

                // Make grid behave like the web table: columns auto size + allow horizontal scroll only when needed
                grid.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                grid.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

                // A little tighter
                grid.RowHeight = 28;



                // Decide which column (if present) should take remaining space (like the web's first column)
                string? fillColumn =
                    ordered.FirstOrDefault(c => c.Equals("OperatingSystem", StringComparison.OrdinalIgnoreCase))
                    ?? ordered.FirstOrDefault();

                foreach (var col in ordered)
                {
                    var colKey = col;

                    // Auto size most columns; only one column stretches to fill remaining space.
                    var width = colKey.Equals(fillColumn, StringComparison.OrdinalIgnoreCase)
                        ? new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.Star)
                        : new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.Auto);

                    var templateCol = new Avalonia.Controls.DataGridTemplateColumn
                    {
                        Header = HumanizeHeader(colKey),
                        Width = width,
                        MaxWidth = colKey.Equals(fillColumn, StringComparison.OrdinalIgnoreCase) ? 340 : 180,
                        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Dictionary<string, string>>((item, ns) =>
                        {
                            var tb = new TextBlock
                            {
                                TextWrapping = TextWrapping.NoWrap,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                Margin = new Thickness(6, 2, 6, 2)
                            };

                            tb.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding
                            {
                                Converter = lookup,
                                ConverterParameter = colKey
                            });

                            return tb;
                        })
                    };

                    grid.Columns.Add(templateCol);
                }


                grid.ItemsSource = rows;
                resultsPanel.Children.Add(grid);
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to parse aggregated response: {ex.Message}\n");
                resultsPanel.Children.Add(new TextBlock { Text = json, TextWrapping = TextWrapping.Wrap });
            }

            // ---- local helpers ----

            static JObject? ExtractBestDataObject(JObject row, HashSet<string> noisyTopLevel)
            {
                // Try these in order
                var candidate =
                    TryGetObjectOrJsonObject(row["Blob"]) ??
                    TryGetObjectOrJsonObject(row["Values"]) ??
                    TryGetObjectOrJsonObject(row["Data"]) ??
                    TryGetObjectOrJsonObject(row["Result"]);

                if (candidate != null && candidate.Properties().Any())
                    return candidate;

                // Fall back to cleaned top-level (exclude noisy internal fields)
                var cleaned = new JObject();
                foreach (var p in row.Properties())
                {
                    if (noisyTopLevel.Contains(p.Name))
                        continue;

                    cleaned[p.Name] = p.Value;
                }

                return cleaned.Properties().Any() ? cleaned : null;
            }

            static JObject? TryGetObjectOrJsonObject(JToken? token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return null;

                if (token is JObject obj)
                    return obj;

                if (token.Type == JTokenType.String)
                {
                    var s = token.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(s))
                        return null;

                    if (s.StartsWith("{") && s.EndsWith("}"))
                    {
                        try { return JObject.Parse(s); }
                        catch { return null; }
                    }
                }

                return null;
            }

            static string ToCellString(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return "";

                // suppress placeholder/default dates
                if (token.Type == JTokenType.Date)
                {
                    try
                    {
                        var dt = token.ToObject<DateTime>();
                        if (dt == DateTime.MinValue || dt.Year <= 1)
                            return "";
                        return dt.ToLocalTime().ToString("g");
                    }
                    catch { }
                }

                // keep nested as compact JSON
                if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                    return token.ToString(Newtonsoft.Json.Formatting.None);

                return token.ToString();
            }

            static string HumanizeHeader(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return s;

                var sb = new System.Text.StringBuilder(s.Length + 8);
                sb.Append(s[0]);

                for (int i = 1; i < s.Length; i++)
                {
                    var ch = s[i];
                    if (char.IsUpper(ch) && !char.IsUpper(s[i - 1]))
                        sb.Append(' ');
                    sb.Append(ch);
                }

                return sb.ToString();
            }
        }



        private sealed class DictLookupConverter : Avalonia.Data.Converters.IValueConverter
        {
            public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            {
                if (parameter is not string key || string.IsNullOrWhiteSpace(key))
                    return "";

                if (value is Dictionary<string, string> dict)
                    return dict.TryGetValue(key, out var s) ? s : "";

                if (value is IDictionary<string, string> idict)
                    return idict.TryGetValue(key, out var s) ? s : "";

                return "";
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }


        private void PopulateFqdnCheckboxList(List<Dictionary<string, string>> results)
        {
            var fqdnListBox = this.FindControl<ListBox>("SelectedFqdnListBox");
            fqdnListBox.ItemsSource = results
                .Select(r => r.TryGetValue("Fqdn", out var fqdn) ? fqdn : null)
                .Where(fqdn => !string.IsNullOrEmpty(fqdn))
                .Distinct()
                .Select(fqdn => new CheckBox { Content = fqdn, IsChecked = false })
                .ToList();
        }



        private void CopyFilteredFqdnsToClipboard(object? sender, RoutedEventArgs e)
        {
            var selectedFqdns = _fqdnCheckboxes
                .Where(kvp => kvp.Value.IsChecked == true)
                .Select(kvp => kvp.Key.Trim())
                .Where(fqdn => !string.IsNullOrWhiteSpace(fqdn))
                .Distinct()
                .OrderBy(fqdn => fqdn)
                .ToList();

            if (selectedFqdns.Count == 0)
            {
                LogToUI("⚠️ No FQDNs selected to copy.\n");
                return;
            }

            var fqdnList = string.Join(Environment.NewLine, selectedFqdns);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            clipboard?.SetTextAsync(fqdnList);
            LogToUI($"📋 Copied {selectedFqdns.Count} selected FQDN(s) to clipboard.\n");
        }

        private void AddPastedFqdnsToList_Click(object? sender, RoutedEventArgs e)
        {
            var textBox = this.FindControl<TextBox>("FqdnPasteBox");
            var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");

            var newFqdns = textBox.Text?
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct()
                .ToList();

            if (newFqdns == null || newFqdns.Count == 0)
            {
                LogToUI("⚠️ No FQDNs entered to add.\n");
                return;
            }

            var currentItems = listBox.Items?.Cast<string>().ToList() ?? new List<string>();
            foreach (var fqdn in newFqdns)
            {
                if (!currentItems.Contains(fqdn))
                    currentItems.Add(fqdn);
            }

            listBox.ItemsSource = currentItems.OrderBy(f => f).ToList();
            this.FindControl<TextBlock>("FqdnCountLabel").Text = $"Selected FQDNs ({currentItems.Count})";
            textBox.Text = string.Empty;
        }

        private void RemoveSelectedFqdns(object? sender, RoutedEventArgs e)
        {
            var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
            var selected = listBox.SelectedItems?.Cast<string>().ToList() ?? new List<string>();
            var remaining = listBox.Items?.Cast<string>().Where(i => !selected.Contains(i)).ToList() ?? new List<string>();

            listBox.ItemsSource = remaining;
            this.FindControl<TextBlock>("FqdnCountLabel").Text = $"Selected FQDNs ({remaining.Count})";
        }

        private async void RunFqdnTabButton_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("⚠️ Not authenticated. Please login.");
                UpdateAuthStatusIndicator(false);
                return;
            }

            if (_selectedInstruction == null)
            {
                LogToUI("❌ No instruction selected to run.\n");
                return;
            }

            // Close the flyout after the run is initiated.
            CloseAttachedFlyout(_fqdnRunFilterButton);

            var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
            var fqdns = listBox.Items?.Cast<string>()
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            if (fqdns == null || fqdns.Count == 0)
            {
                LogToUI("❌ No FQDNs selected to run against.\n");
                return;
            }

            // Hard limit for Instruction Runner (keeps the app responsive and avoids accidental large runs).
            int maxTargets = 10;
            try { maxTargets = Math.Max(1, _configHelper?.GetIntSetting("MaxFqdnTargets", 10) ?? 10); } catch { maxTargets = 10; }
            if (fqdns.Count > maxTargets)
            {
                LogToUI($"⚠️ Limiting run to first {maxTargets} FQDN(s) (configured MaxFqdnTargets).");
                fqdns = fqdns.Take(maxTargets).ToList();
            }

            var fqdnParamPanel = TryFindControl<StackPanel>("FqdnParametersPanel");
            var paramDict = new Dictionary<string, string>();

            foreach (var row in fqdnParamPanel.Children.OfType<StackPanel>())
            {
                // Match the same parameter extraction logic used by OnRunClicked (parameters are rendered inside a Border).
                if (row.Children.Count >= 2 &&
                    row.Children[1] is Border border &&
                    border.Child is Control input &&
                    input.Tag is string paramName)
                {
                    string value = input switch
                    {
                        ComboBox combo => combo.SelectedItem?.ToString() ?? "",
                        TextBox textBox => textBox.Text,
                        _ => ""
                    };

                    if (!string.IsNullOrWhiteSpace(value))
                        paramDict[paramName] = value;
                }
            }
            int.TryParse(_instructionTtlBox?.Text, out var instructionTtl);
            int.TryParse(_responseTtlBox?.Text, out var responseTtl);
            if (instructionTtl <= 0) instructionTtl = 60;
            if (responseTtl <= 0) responseTtl = 120;

            // Track TTL countdown for Results tab
            _currentInstructionRunStartedUtc = DateTime.UtcNow;
            _currentInstructionTtlMinutes = instructionTtl;
            UpdateResultsTtlCountdownUi();

            var payload = new Dictionary<string, object>
            {
                ["DefinitionId"] = _selectedInstruction.Id,
                ["KeepRaw"] = 1,
                // Match the same exact run format used by the main Run button (OnRunClicked):
                // "Parameters" is a list of { Name, Value } objects.
                ["Parameters"] = paramDict.Select(p => new Dictionary<string, string>
                {
                    ["Name"] = p.Key,
                    ["Value"] = p.Value
                }).ToList(),
                ["InstructionTtlMinutes"] = instructionTtl,
                ["ResponseTtlMinutes"] = responseTtl,
                ["ReadablePayload"] = _selectedInstruction.Name,
                ["Devices"] = fqdns,
                ["Scope"] = new { }
            };

            // Apply server-side ResultsFilter when Run scope is set to Filtered (FQDN tab)
            if (_fqdnRunScopeFilteredRadio?.IsChecked == true)
            {
                var rf = BuildRunResultsFilterObject(isFqdnTab: true);
                payload["ResultsFilter"] = rf; // can be null (API will ignore)
            }
            else
            {
                payload["ResultsFilter"] = null;
            }


            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                string url = $"https://{_selectedPlatform?.Url}/consumer/Instructions/Targeted";
                string payloadJson = JsonConvert.SerializeObject(payload);
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                System.Net.HttpStatusCode? httpStatus = null;
                var json = await ApiLogger.LogApiCallAsync(
                    label: "RunFQDNInstruction",
                    endpoint: "Consumer/Instructions/Targeted",
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync(url, content);
                        httpStatus = response.StatusCode;
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: payloadJson
                );

                if (httpStatus == System.Net.HttpStatusCode.Unauthorized)
                {
                    ForceUnauthenticatedState("RunFQDNInstruction returned 401");
                    return;
                }

                if (httpStatus == null || ((int)httpStatus.Value < 200 || (int)httpStatus.Value >= 300))
                {
                    LogToUI($"❌ Instruction execution failed: {httpStatus}\n");
                    if (!string.IsNullOrWhiteSpace(json))
                        LogToUI($"📬 Response body: {json}\n");
                    return;
                }

                LogToUI($"✅ Instruction sent to {fqdns.Count} FQDN(s).\n");

                int responseId;
                try
                {
                    var responseObj = JsonConvert.DeserializeObject<dynamic>(json);
                    responseId = responseObj?.Id ?? _selectedInstruction.Id;
                }
                catch
                {
                    responseId = _selectedInstruction.Id;
                }

                _lastInstructionId = responseId;
                _lastInstructionDefinition = _selectedInstruction;

                try
                {
                    UpdateViewToggles(_lastInstructionDefinition);
                    if (ResultsContextInstructionName != null) ResultsContextInstructionName.Text = _lastInstructionDefinition?.Name ?? string.Empty;
                }
                catch { }

                // Canonical active instruction id for Results live polling (required for auto-refresh).
                _activeResultsInstructionId = _lastInstructionId;
                _activeResultsExpiresUtc = null;
                _lastProgressSentCount = -1;
                _lastProgressReceivedCount = -1;
                _lastProgressOutstandingCount = -1;
                _lastPeriodicResultsRefreshUtc = DateTime.MinValue;
                _lastAutoResultsRefreshUtc = DateTime.MinValue;
                _lastResultsReceivedIncreaseUtc = DateTime.UtcNow;

                // IMPORTANT: The Results tab content is lazily created. If we try to render Raw view before
                // switching tabs, ResultsPanel can be null and nothing appears until the user clicks Refresh.
                // Select the Results tab *before* loading/rendering the first page.
                var tabControlPre = this.FindControl<TabControl>("MainTabControl");
                if (tabControlPre != null)
                {
                    tabControlPre.SelectedIndex = 3; // Results
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }

                // Ensure Results tab status + summary begin updating immediately
                _currentInstructionId = _lastInstructionId.ToString();
                _resultsStatusPollRunId++;
                var runId = _resultsStatusPollRunId;
                _ = RefreshInstructionStatusAsync(_currentInstructionId, runId);
                _ = ShowInstructionProgressAsync(_lastInstructionId, log: false);

                // Reset paging and load first page in the Results tab.
                // IMPORTANT: When running from the FQDN List tab, make sure we await the async view rebuild.
                ResetResultsPagingState();
                _resultsPageSize = GetResultsPageSize();
                await LoadInstructionResultsPageAsync(_lastInstructionId, startRange: "0;0", resetPanels: true);
                await LoadInstructionHistoryAsync();

                await UpdateView("Raw");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to run instruction: {ex.Message}\n");
            }
        }


        public void UpdateAuthStatusIndicator(bool isAuthenticated)
        {
            // Use existing theme resources (no hard-coded colors).
            // If the resource is a Color instead of an IBrush, wrap it in a SolidColorBrush.
            IBrush? okBrush = null;
            IBrush? badBrush = null;

            try
            {
                var okObj = Application.Current?.FindResource("SuccessBrush");
                if (okObj is IBrush ob) okBrush = ob;
                else if (okObj is Color oc) okBrush = new SolidColorBrush(oc);

                var badObj = Application.Current?.FindResource("ErrorBrush");
                if (badObj is IBrush bb) badBrush = bb;
                else if (badObj is Color bc) badBrush = new SolidColorBrush(bc);
            }
            catch
            {
                // ignore
            }

            if (_authStatusText != null)
            {
                _authStatusText.Text = isAuthenticated ? "🟢 Authenticated" : "🔴 Not Authenticated";

                var brush = isAuthenticated ? okBrush : badBrush;
                if (brush != null)
                    _authStatusText.Foreground = brush;
            }

            if (_loginButton != null) _loginButton.IsEnabled = !isAuthenticated;
            if (_logoutButton != null) _logoutButton.IsEnabled = isAuthenticated;

            // Auth gating for actions and polling.
            try
            {
                UpdateAuthGatedUi(isAuthenticated);
            }
            catch
            {
                // ignore
            }
        }


        private void UpdateAuthGatedUi(bool isAuthenticated)
        {
            // Disable/enable auth-required actions.
            try { if (_runButton != null) _runButton.IsEnabled = isAuthenticated; } catch { }
            try { if (_exportButton != null) _exportButton.IsEnabled = isAuthenticated; } catch { }
            try { if (_exportAllButton != null) _exportAllButton.IsEnabled = isAuthenticated; } catch { }
            try { if (_refreshHistoryButton != null) _refreshHistoryButton.IsEnabled = isAuthenticated; } catch { }
            try { if (_rerunInstructionButton != null) _rerunInstructionButton.IsEnabled = isAuthenticated; } catch { }
            try { if (_cancelInstructionButton != null) _cancelInstructionButton.IsEnabled = isAuthenticated; } catch { }

            // FQDN tab run button
            try
            {
                var fqdnRun = this.FindControl<Button>("RunFqdnTabButton");
                if (fqdnRun != null) fqdnRun.IsEnabled = isAuthenticated;
            }
            catch { }

            // Approvals UI
            try { if (NotificationAlertButton != null) NotificationAlertButton.IsEnabled = isAuthenticated; } catch { }

            // Start/stop polling.
            if (isAuthenticated)
                StartNotificationTimerIfAuthenticated();
            else
                StopNotificationTimer();
        }



        private void ToggleResultsLoading(bool show)
        {
            this.FindControl<StackPanel>("LoadingStatusPanel").IsVisible = show;
        }

        // Add this method in your MainWindow.xaml.cs

        private async void TodayButton_Click(object sender, RoutedEventArgs e)
        {
            // Ensure HistoryDatePicker is initialized
            if (_historyDatePicker != null)
            {
                // Set the HistoryDatePicker to today's date when the Today button is clicked
                _historyDatePicker.SelectedDate = DateTime.Today;

                // Optionally log this action
                LogToUI($"📅 History filter set to today's date: {DateTime.Today.ToShortDateString()}.\n");

                // Refresh the instruction history based on the new date
                await LoadInstructionHistoryAsync();
            }
            else
            {
                LogToUI("❌ HistoryDatePicker is not initialized.\n");
            }
        }




        private async Task ShowInstructionProgressAsync(int instructionId, bool log = false)
        {
            var loadingPanel = this.FindControl<StackPanel>("LoadingStatusPanel");
            if (loadingPanel != null)
                loadingPanel.IsVisible = true;

            try
            {
                if (log)
                {
                    // LogToUI($"⏳ Fetching instruction progress for ID {instructionId}...\n");
                }

                if (_isLegacyVersion)
                {
                    await ShowLegacyInstructionProgressAsync(instructionId, log);
                }
                else
                {
                    await ShowModernInstructionProgressAsync(instructionId, log);
                }
            }
            catch (Exception ex)
            {
                if (log)
                    LogToUI($"❌ Error fetching progress: {ex.Message}\n");
            }
            finally
            {
                if (loadingPanel != null)
                    loadingPanel.IsVisible = false;
            }

        }




        private async Task ShowLegacyInstructionProgressAsync(int instructionId, bool log = false)
        {
            try
            {
                string endpoint = $"consumer/InstructionStatistics/Combined/{instructionId}";
                string url = $"https://{_selectedPlatform?.Url}/{endpoint}";

                if (log)
                    LogToUI($"📊 (Legacy) Fetching stats from: {url}\n");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var json = await ApiLogger.LogApiCallAsync(
                    label: "LegacyProgressStats",
                    endpoint: endpoint,
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            LogToUI($"❌ Legacy progress failed: {response.StatusCode}\n");
                        }
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: ""  // No payload for GET
                );

                JObject parsed;
                try
                {
                    parsed = JObject.Parse(json);
                }
                catch
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var summaryLabel = this.FindControl<TextBlock>("ProgressSummaryLabel");
                        var countsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");
                        var border = this.FindControl<Border>("ProgressSummaryBorder");
                        if (summaryLabel != null && countsLabel != null)
                        {
                            summaryLabel.Text = "📊 Instruction Progress Summary";
                            countsLabel.Text = "(progress stats unavailable)";
                            UpdateProgressCountsFields(0, 0, 0, 0, 0, 0, 0, 0, "(progress stats unavailable)");
                            if (border != null) border.IsVisible = true;
                        }
                    }, DispatcherPriority.Background);
                    return;
                }

                var summary = parsed["Summary"];
                if (summary == null)
                {
                    if (log)
                        LogToUI("⚠️ No summary returned from progress API.\n");
                    return;
                }

                int sent = summary["SentCount"]?.ToObject<int>() ?? 0;
                int received = summary["ReceivedCount"]?.ToObject<int>() ?? 0;
                int outstanding = summary["OutstandingResponsesCount"]?.ToObject<int>() ?? 0;
                // Status + auto-refresh logic (Sent vs Received; ignore Outstanding for status)
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateResultsStatusFromProgress(sent, received);
                }, DispatcherPriority.Background);

                await MaybeAutoRefreshResultsFromProgressAsync(instructionId, sent, received);

                int rowInserts = summary["TotalRowInserts"]?.ToObject<int>() ?? 0;
                if (rowInserts <= 0)
                    rowInserts = summary["TotalRowProcessed"]?.ToObject<int>() ?? 0;

                long bytesSent = summary["TotalBytesSent"]?.ToObject<long>() ?? 0L;
                long bytesReceived = summary["TotalBytesReceived"]?.ToObject<long>() ?? 0L;

                string avgExec = summary["AverageExecTime"]?.ToString() ?? "";
                string runtime = summary["Runningtime"]?.ToString() ?? "";

                int success = summary["TotalSuccessRespondents"]?.ToObject<int>() ?? 0;
                int noData = summary["TotalSuccessNoDataRespondents"]?.ToObject<int>() ?? 0;
                int notImplemented = summary["TotalNotImplementedRespondents"]?.ToObject<int>() ?? 0;
                int failed = summary["TotalErrorRespondents"]?.ToObject<int>() ?? 0;

                int online = summary["OnlineDeviceCount"]?.ToObject<int>() ?? 0;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var summaryLabel = this.FindControl<TextBlock>("ProgressSummaryLabel");
                        var countsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");
                        var border = this.FindControl<Border>("ProgressSummaryBorder");

                        _resultsTotalRowsExpected = (outstanding == 0 ? rowInserts : 0);

                        _resultsOutstandingCount = outstanding;
                        UpdateResultsLoadingIndicator();

                        if (summaryLabel != null && countsLabel != null)
                        {
                            summaryLabel.Text = "📊 Instruction Progress Summary";
                            countsLabel.Text =
                                $"Online: {online}, Sent: {sent}, Received: {received}, Outstanding: {outstanding}, " +
                                $"Success: {success}, No Data: {noData}, Not Implemented: {notImplemented}, Errors: {failed}";

                            UpdateProgressCountsFields(online, sent, received, outstanding, success, noData, notImplemented, failed);

                            // Theme-safe callout emphasis when errors are present.
                            if (border != null)
                            {
                                border.BorderBrush = failed > 0 ? (IBrush)(this.FindResource("ErrorBrush") ?? Brushes.Red) : (IBrush)(this.FindResource("ThemeBorderLowBrush") ?? Brushes.Gray);
                                border.IsVisible = true;
                            }

                            countsLabel.FontWeight = failed > 0 ? FontWeight.Bold : FontWeight.Normal;
                        }
                        else if (log)
                        {
                            LogToUI("⚠️ Progress panel controls not found in UI.\n");
                        }
                    }
                    catch (Exception uiEx)
                    {
                        LogToUI($"⚠️ UI update error: {uiEx.Message}\n");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                if (log)
                    LogToUI($"❌ Exception fetching legacy progress: {ex.Message}\n");
            }
        }


        private async Task MaybeAutoRefreshResultsFromProgressAsync(int instructionId, int sent, int received)
        {
            try
            {
                if (instructionId <= 0)
                    return;

                // Only refresh when we're already looking at this instruction's results.
                if (_activeResultsInstructionId <= 0 || instructionId != _activeResultsInstructionId)
                    return;

                if (_resultsPageLoading || _autoResultsRefreshInFlight)
                    return;

                // Establish baseline on first poll (no refresh).
                if (_lastProgressReceivedCount < 0 || _lastProgressSentCount < 0)
                {
                    _lastProgressSentCount = sent;
                    _lastProgressReceivedCount = received;

                    // Treat the first poll as "active" so RefreshInstructionStatusAsync uses the fast cadence.
                    _lastResultsReceivedIncreaseUtc = DateTime.UtcNow;
                    _lastPeriodicResultsRefreshUtc = DateTime.MinValue;
                    return;
                }

                bool receivedIncreased = received > _lastProgressReceivedCount;
                if (receivedIncreased)
                    _lastResultsReceivedIncreaseUtc = DateTime.UtcNow;
                _lastProgressReceivedCount = received;
                _lastProgressSentCount = sent;

                var remaining = GetActiveResultsTtlRemaining();
                bool ttlActive = remaining.HasValue && remaining.Value > TimeSpan.Zero;

                // Periodically refresh while TTL is still active so results feel "live"
                // even if counts don't change on every poll.
                var sinceIncreaseForPeriodic = _lastResultsReceivedIncreaseUtc == DateTime.MinValue
                    ? TimeSpan.Zero
                    : (DateTime.UtcNow - _lastResultsReceivedIncreaseUtc);

                double periodicSeconds;
                if (sinceIncreaseForPeriodic < TimeSpan.FromSeconds(30))
                    periodicSeconds = 5;
                else if (sinceIncreaseForPeriodic < TimeSpan.FromMinutes(2))
                    periodicSeconds = 15;
                else if (sinceIncreaseForPeriodic < TimeSpan.FromMinutes(5))
                    periodicSeconds = 30;
                else
                    periodicSeconds = 60;

                bool periodicRefreshDue = ttlActive && (DateTime.UtcNow - _lastPeriodicResultsRefreshUtc).TotalSeconds >= periodicSeconds;

                if (!receivedIncreased && !periodicRefreshDue)
                    return;

                var nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastAutoResultsRefreshUtc).TotalSeconds < 2)
                    return;

                _autoResultsRefreshInFlight = true;
                try
                {
                    _lastAutoResultsRefreshUtc = nowUtc;
                    if (periodicRefreshDue)
                        _lastPeriodicResultsRefreshUtc = nowUtc;

                    // Keep current range; do not reset panels.
                    await LoadInstructionResultsPageAsync(instructionId, _resultsCurrentRange, resetPanels: false);
                }
                finally
                {
                    _autoResultsRefreshInFlight = false;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateResultsStatusFromProgress(int sent, int received)
        {
            try
            {
                var statusText = this.FindControl<TextBlock>("ResultsStatusText");
                if (statusText == null)
                    return;

                if (sent <= 0 && received <= 0)
                    return;

                string computed;

                // Status driven by Sent vs Received (ignore Outstanding to avoid flicker / legacy mismatch)
                if (sent > 0 && received == 0)
                    computed = "Sent";
                else if (received > 0 && received < sent)
                    computed = "In Progress";
                else if (sent > 0 && received >= sent)
                    computed = "Completed";
                else if (received > 0)
                    computed = "In Progress";
                else
                    computed = "Sent";

                var remaining = GetActiveResultsTtlRemaining();
                string suffix = "";
                if (remaining.HasValue && remaining.Value > TimeSpan.Zero)
                {
                    var remText = FormatTtlRemaining(remaining.Value);
                    if (computed.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                        suffix = $" – may receive more results for {remText}";
                    else
                        suffix = $" – TTL remaining: {remText}";
                }

                string desired;
                if (sent > 0)
                    desired = $"Status: {computed} ({received} / {sent}){suffix}";
                else
                    desired = $"Status: {computed}{suffix}";

                if (!string.Equals(statusText.Text, desired, StringComparison.Ordinal))
                    statusText.Text = desired;
            }
            catch
            {
                // Never let status updates break polling
            }
        }

        private void UpdateProgressCountsFields(int online, int sent, int received, int outstanding, int success, int noData, int notImplemented, int errors, string? fallbackText = null)
        {
            try
            {
                var onlineTb = this.FindControl<TextBlock>("ProgressOnlineValue");
                var sentTb = this.FindControl<TextBlock>("ProgressSentValue");
                var receivedTb = this.FindControl<TextBlock>("ProgressReceivedValue");
                var outstandingTb = this.FindControl<TextBlock>("ProgressOutstandingValue");
                var successTb = this.FindControl<TextBlock>("ProgressSuccessValue");
                var noDataTb = this.FindControl<TextBlock>("ProgressNoDataValue");
                var notImplTb = this.FindControl<TextBlock>("ProgressNotImplementedValue");
                var errorsTb = this.FindControl<TextBlock>("ProgressErrorsValue");

                if (onlineTb != null) onlineTb.Text = online.ToString();
                if (sentTb != null) sentTb.Text = sent.ToString();
                if (receivedTb != null) receivedTb.Text = received.ToString();
                if (outstandingTb != null) outstandingTb.Text = outstanding.ToString();
                if (successTb != null) successTb.Text = success.ToString();
                if (noDataTb != null) noDataTb.Text = noData.ToString();
                if (notImplTb != null) notImplTb.Text = notImplemented.ToString();
                if (errorsTb != null) errorsTb.Text = errors.ToString();

                var countsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");
                if (countsLabel != null)
                {
                    countsLabel.Text = string.IsNullOrWhiteSpace(fallbackText)
                        ? $"Online: {online}, Sent: {sent}, Received: {received}, Outstanding: {outstanding}, Success: {success}, No Data: {noData}, Not Implemented: {notImplemented}, Errors: {errors}"
                        : fallbackText;
                }
            }
            catch
            {
                // ignore
            }
        }

        private string BuildRunResultsFilterSummary(bool isFqdnTab)
        {
            var clauses = isFqdnTab ? _activeFqdnRunFilters : _activeInstrRunFilters;
            if (clauses == null || clauses.Count == 0)
                return "(none)";

            static string Quote(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "\"\"";
                return s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
            }

            return string.Join(", ",
                clauses
                    .Where(c => !string.IsNullOrWhiteSpace(c.Column) && !string.IsNullOrWhiteSpace(c.Value))
                    .Select(c => $"{c.Column} {c.Operator} {Quote(c.Value)}"));
        }

        private string BuildResultsCoverageLabel(bool isFqdnTab)
        {
            if (isFqdnTab)
            {
                if (!string.IsNullOrWhiteSpace(_resultsCoverageOverride) && _resultsCoverageOverrideInstructionId == _lastInstructionId)
                    return _resultsCoverageOverride;

                var listBox = this.FindControl<ListBox>("SelectedFqdnListBox");
                if (listBox?.Items is IEnumerable items)
                {
                    var fqdns = items.Cast<object>()
                        .Select(x => x?.ToString() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToList();

                    if (fqdns.Count > 0)
                        return $"FQDN == {string.Join(", ", fqdns)}";
                }

                return "FQDN list";
            }

            // If we have a server-provided coverage string for the currently active instruction, prefer it.
            // This prevents the polling loop from constantly resetting the UI to generic "All devices"/MG text.
            if (!string.IsNullOrWhiteSpace(_resultsCoverageOverride) && _resultsCoverageOverrideInstructionId == _lastInstructionId)
                return _resultsCoverageOverride;

            if (_runScopeFilteredRadio?.IsChecked == true)
                return "Filtered scope";

            if (_managementGroupComboBox?.SelectedItem is ManagementGroup mg)
                return $"Management group: {mg.Name}";

            return "All devices";
        }



        private async Task ShowModernInstructionProgressAsync(int instructionId, bool log = false)
        {
            string endpoint = $"consumer/InstructionStatistics/Combined/{instructionId}";
            string url = $"https://{_selectedPlatform?.Url}/{endpoint}";

            if (log)
                LogToUI($"📊 (Modern) Fetching progress from: {url}\n");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var json = await ApiLogger.LogApiCallAsync(
                    label: "ModernProgressStats",
                    endpoint: endpoint,
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode && log)
                        {
                            LogToUI($"❌ Modern progress failed: {response.StatusCode}\n");
                        }
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: ""
                );

                JObject parsed;
                try
                {
                    parsed = JObject.Parse(json);
                }
                catch
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var summaryLabel = this.FindControl<TextBlock>("ProgressSummaryLabel");
                        var countsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");
                        var border = this.FindControl<Border>("ProgressSummaryBorder");
                        if (summaryLabel != null && countsLabel != null)
                        {
                            summaryLabel.Text = "📊 Instruction Progress Summary";
                            countsLabel.Text = "(progress stats unavailable)";
                            UpdateProgressCountsFields(0, 0, 0, 0, 0, 0, 0, 0, "(progress stats unavailable)");
                            if (border != null) border.IsVisible = true;
                        }
                    }, DispatcherPriority.Background);
                    return;
                }

                var summary = parsed["Summary"];
                if (summary == null)
                {
                    if (log)
                        LogToUI("⚠️ No summary returned from progress API.\n");
                    return;
                }

                int sent = summary["SentCount"]?.ToObject<int>() ?? 0;
                int received = summary["ReceivedCount"]?.ToObject<int>() ?? 0;
                int outstanding = summary["OutstandingResponsesCount"]?.ToObject<int>() ?? 0;

                Dispatcher.UIThread.Post(() =>
                {
                    UpdateResultsStatusFromProgress(sent, received);
                }, DispatcherPriority.Background);

                await MaybeAutoRefreshResultsFromProgressAsync(instructionId, sent, received);


                int rowInserts = summary["TotalRowInserts"]?.ToObject<int>() ?? 0;
                if (rowInserts <= 0)
                    rowInserts = summary["TotalRowProcessed"]?.ToObject<int>() ?? 0;

                long bytesSent = summary["TotalBytesSent"]?.ToObject<long>() ?? 0L;
                long bytesReceived = summary["TotalBytesReceived"]?.ToObject<long>() ?? 0L;

                string avgExec = summary["AverageExecTime"]?.ToString() ?? "";
                string runtime = summary["Runningtime"]?.ToString() ?? "";

                int success = summary["TotalSuccessRespondents"]?.ToObject<int>() ?? 0;
                int noData = summary["TotalSuccessNoDataRespondents"]?.ToObject<int>() ?? 0;
                int notImplemented = summary["TotalNotImplementedRespondents"]?.ToObject<int>() ?? 0;
                int failed = summary["TotalErrorRespondents"]?.ToObject<int>() ?? 0;

                int online = summary["OnlineDeviceCount"]?.ToObject<int>() ?? 0;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var summaryLabel = this.FindControl<TextBlock>("ProgressSummaryLabel");
                        var countsLabel = this.FindControl<TextBlock>("ProgressCountsLabel");
                        var border = this.FindControl<Border>("ProgressSummaryBorder");

                        _resultsTotalRowsExpected = (outstanding == 0 ? rowInserts : 0);

                        _resultsOutstandingCount = outstanding;
                        UpdateResultsLoadingIndicator();

                        if (summaryLabel != null && countsLabel != null)
                        {
                            summaryLabel.Text = "📊 Instruction Progress Summary";
                            countsLabel.Text =
                                $"Online: {online}, Sent: {sent}, Received: {received}, Outstanding: {outstanding}, " +
                                $"Success: {success}, No Data: {noData}, Not Implemented: {notImplemented}, Errors: {failed}";

                            UpdateProgressCountsFields(online, sent, received, outstanding, success, noData, notImplemented, failed);
                            if (border != null)
                            {
                                border.IsVisible = true;
                                border.BorderBrush = failed > 0 ? (TryGetThemeBrush("ErrorBrush") ?? Brushes.Red) : (TryGetThemeBrush("ThemeBorderLowBrush") ?? Brushes.Gray);
                            }
                            countsLabel.FontWeight = failed > 0 ? FontWeight.Bold : FontWeight.Normal;
                        }
                        else if (log)
                        {
                            LogToUI("⚠️ Progress panel controls not found in UI.\n");
                        }
                    }
                    catch (Exception uiEx)
                    {
                        LogToUI($"⚠️ UI update error: {uiEx.Message}\n");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                if (log)
                    LogToUI($"❌ Exception fetching modern progress: {ex.Message}\n");
            }
        }

        private async Task OnPreviewTargetsClicked()
        {
            if (_selectedInstruction == null)
            {
                LogToUI("❌ No instruction selected to preview.\n");
                return;
            }

            var scopeQuery = new JObject
            {
                ["Operator"] = "AND",
                ["Operands"] = new JArray
        {
            new JObject { ["Attribute"] = "OsType", ["Operator"] = "==", ["Value"] = "Windows" },
            new JObject { ["Attribute"] = "DevType", ["Operator"] = "==", ["Value"] = "Desktop" }
        }
            };

            var payload = new JObject
            {
                ["instructionDefinitionId"] = _selectedInstruction.Id,
                ["scopeQuery"] = scopeQuery,
                ["Expression"] = scopeQuery
            };

            var payloadJson = payload.ToString();
            var endpoint = "Consumer/SystemStatistics/ProjectedInstructionStatistics";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                var json = await ApiLogger.LogApiCallAsync(
                    label: "PreviewTargets",
                    endpoint: endpoint,
                    apiCall: async () =>
                    {
                        var response = await client.PostAsync($"https://{_selectedPlatform?.Url}/{endpoint}", content);
                        if (!response.IsSuccessStatusCode)
                        {
                            LogToUI($"❌ Preview API error: {response.StatusCode}\n");
                        }
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: payloadJson
                );

                var result = JObject.Parse(json);

                int estimated = result["EstimatedCount"]?.Value<int>() ?? 0;
                int online = result["EstimatedSuccessRespondents"]?.Value<int>() ?? 0;
                int offline = estimated - online;

                var panel = this.FindControl<StackPanel>("TargetStatsPanel");
                panel.Children.Clear();

                var label = new TextBlock
                {
                    Text = $"📌 Estimated Targets: {estimated:N0}   Online: {online:N0}   Offline: {offline:N0}",
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.DarkSlateBlue,
                    Margin = new Thickness(4)
                };

                panel.Children.Add(label);
                LogToUI($"📊 Preview results: Estimated = {estimated}, Online = {online}, Offline = {offline}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to preview targets: {ex.Message}\n");
            }
        }

        private async Task AuthenticateAsync()
        {
            if (_selectedPlatform == null)
            {
                LogToUI("⚠️ No platform selected for authentication.\n");
                UpdateAuthStatusIndicator(false);
                return;
            }

            LogToUI($"🔐 Authenticating with platform: {_selectedPlatform.Url}\n");

            var result = await _authService.AuthenticateAsync(_selectedPlatform);

            _authService.Token = result?.Token;
            _token = _authService.Token;

            var expiration = DumpJwtPayload(_token);
            if (expiration != null)
            {
                _authService.SetExpirationTime(expiration.Value);
                LogToUI($"🔍 Expiration: {expiration:u}\n");
                var timeRemaining = expiration.Value - DateTime.UtcNow;
                string remainingText = $"{(int)timeRemaining.TotalMinutes}m {timeRemaining.Seconds:D2}s";
                _authService.NotifyTokenTimeRemaining(timeRemaining);

                // ✅ NEW: prevent expired tokens from being used
                if (expiration.Value <= DateTime.UtcNow)
                {
                    LogToUI("❌ Token is already expired. Reauthenticating...\n");
                    _authService.DeleteSavedToken(_selectedPlatform.Url);
                    await _authService.LogoutAsync(); // Optional: cleanup if needed
                    await AuthenticateAsync(); // Retry recursively with fresh login
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(_token))
            {
                //LogToUI($"🔑 Token (first 40 chars): {_token.Substring(0, Math.Min(_token.Length, 40))}...\n");
                LogToUI($"3🧪AuthenticateAsync Token Part Count: {_token.Split('.').Length}\n");

                if (expiration != null)
                {
                    var timeRemaining = expiration.Value - DateTime.UtcNow;
                    LogToUI($"🕓 Token Expiration:  {expiration:u} ({timeRemaining.TotalMinutes:F1} mins from now)\n");
                }
            }

            var defaultMg = _selectedPlatform.DefaultMG;
            if (!string.IsNullOrEmpty(defaultMg))
            {
                _managementGroupComboBox.SelectedItem = defaultMg;
                LogToUI($"✅ Loaded default MG: '{defaultMg}' for {_selectedPlatform.Url}\n");
            }

            if (string.IsNullOrWhiteSpace(_token))
            {
                LogToUI("❌ Authentication failed.\n");
                UpdateAuthStatusIndicator(false);
                return;
            }

            LogToUI("✅ Authenticated successfully.\n");
            UpdateAuthStatusIndicator(true);

            await LoadInstructions();
            await LoadManagementGroupsAsync();
            await LoadInstructionHistoryAsync();
        }
        private async void ExperienceExportShownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestExperienceResults == null || !_latestExperienceResults.Any())
            {
                LogToUI("⚠️ No Experience results to export.\n");
                return;
            }

            string selectedFormat = GetConfiguredExportFormatOrDefault("csv");

            var dialog = new SaveFileDialog
            {
                Title = "Export Experience Results",
                InitialFileName = $"experience_results.{selectedFormat}",
                DefaultExtension = selectedFormat
            };

            var filePath = await dialog.ShowAsync(this);
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (!filePath.EndsWith($".{selectedFormat}", StringComparison.OrdinalIgnoreCase))
                filePath += $".{selectedFormat}";

            var stringifiedResults = _latestExperienceResults
                .Select(dict => dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.ToString() ?? string.Empty))
                .ToList();

            await ExportHelper.ExportDictionaryListAsync(stringifiedResults, filePath, selectedFormat, _logTextBox);
        }

        private async void ExperienceExportAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_experienceMeasures == null || !_experienceMeasures.Any())
            {
                LogToUI("⚠️ No metrics available for export.\n");
                return;
            }

            string selectedFormat = GetConfiguredExportFormatOrDefault("xlsx");

            var dialog = new SaveFileDialog
            {
                Title = "Export All Experience Results",
                InitialFileName = $"experience_results_all.{selectedFormat}",
                DefaultExtension = selectedFormat
            };

            var filePath = await dialog.ShowAsync(this);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // ✅ Use the selected checkboxes from the actual experience metric map
            var selectedMetrics = new List<string>();
            foreach (var kvp in _experienceCheckboxMap)
            {
                if (kvp.Value.IsChecked == true)
                    selectedMetrics.Add(kvp.Key);
            }

            if (selectedMetrics.Count == 0)
            {
                LogToUI("⚠️ No experience metrics selected.\n");
                return;
            }

            // ✅ Get active filters from input fields
            var filtersDict = new Dictionary<string, List<string>>();
            foreach (var kvp in _experienceFilterInputs)
            {
                var value = kvp.Value?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    filtersDict[kvp.Key] = new List<string> { value };
            }

            // ✅ Use FQDN override if any are entered
            var fqdnInput = _experienceFqdnBox.Text?
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            if (fqdnInput != null && fqdnInput.Any())
            {
                filtersDict = new Dictionary<string, List<string>>
        {
            { "Fqdn", fqdnInput }
        };
            }

            LogToUI($"📤 Starting export to {selectedFormat.ToUpper()}...\n");
            LogToUI($"📎 Last used filters:\n{JsonConvert.SerializeObject(_lastUsedExperienceFilters, Formatting.Indented)}\n");

            var exportPanel = this.FindControl<StackPanel>("ExportStatusPanel");
            var exportBar = this.FindControl<ProgressBar>("ExportProgressBar");
            var exportLabel = this.FindControl<TextBlock>("ExportProgressLabel");

            if (exportPanel != null) exportPanel.IsVisible = true;
            if (exportBar != null) exportBar.Value = 0;
            if (exportLabel != null) exportLabel.Text = "Exporting...";
            var stopwatch = Stopwatch.StartNew(); // Add this near the top of your export method

            var progress = new Progress<(int RowCount, int TotalCount, double ElapsedSeconds)>(state =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    int rowCount = state.RowCount;
                    int totalCount = state.TotalCount;
                    double elapsedSeconds = state.ElapsedSeconds;

                    double percent = totalCount > 0 ? (rowCount * 100.0 / totalCount) : 0;

                    // Estimate ETA
                    if (percent > 0)
                    {
                        double estimatedTotalSeconds = elapsedSeconds / (percent / 100.0);
                        double remainingSeconds = estimatedTotalSeconds - elapsedSeconds;
                        TimeSpan eta = TimeSpan.FromSeconds(remainingSeconds);

                        ExportProgressLabel.Text =
                            $"Exported {rowCount:N0} of {totalCount:N0} rows ({percent:N1}%) – ETA: {eta:mm\\:ss}";
                    }
                    else
                    {
                        ExportProgressLabel.Text = $"Exported {rowCount:N0} of {totalCount:N0} rows...";
                    }

                    ExportProgressBar.Value = percent;
                });
            });



            ExportStatusPanel.IsVisible = true;
            ExportProgressBar.Value = 0;


            await ExportHelper.ExportExperienceResultsAsync(
                _selectedPlatform?.Url,
                _token,
                _lastUsedExperienceMetrics,
                _lastUsedExperienceFilters,
                filePath,
                selectedFormat,
                _logTextBox,
                progress);
            ExportStatusPanel.IsVisible = false;
        }
        private void OnCancelExportClicked(object? sender, RoutedEventArgs e)
        {
            LogToUI("🛑 Canceling export...");
            // ExportHelper.CancelExport();
            this.FindControl<StackPanel>("ExportStatusPanel").IsVisible = false;
        }
        private (Dictionary<string, List<string>> Filters, List<string> Metrics) GetCurrentExperienceFilterState()
        {
            var selectedMetrics = new List<string>();
            foreach (var kvp in _experienceCheckboxMap)
            {
                if (kvp.Value.IsChecked == true)
                    selectedMetrics.Add(kvp.Key);
            }

            if (selectedMetrics.Count == 0)
            {
                // Default measures if the checkbox map wasn't ready / nothing detected as checked.
                selectedMetrics.AddRange(new[]
                {
            "ExperienceScore",
            "StabilityScore",
            "PerformanceScore",
            "ResponsivenessScore",
            "Status"
        });
            }

            var filtersDict = new Dictionary<string, List<string>>();
            foreach (var kvp in _experienceFilterInputs)
            {
                var value = kvp.Value?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    filtersDict[kvp.Key] = new List<string> { value };
            }

            // Optional override from FQDN textbox
            var fqdnList = _experienceFqdnBox?.Text?
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            if (fqdnList?.Any() == true)
            {
                filtersDict = new Dictionary<string, List<string>>
        {
            { "Fqdn", fqdnList }
        };
            }

            return (filtersDict, selectedMetrics);
        }




        private void WriteToLogFile(string message)
        {
            try
            {
                string logDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "Logs");
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                string logFilePath = System.IO.Path.Combine(logDirectory, $"log_{DateTime.Now:yyyyMMdd}.Log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";

                File.AppendAllText(logFilePath, logEntry);
            }
            catch
            {
                // Ignore log writing errors (disk full, permission issues, etc.)
            }
        }

        private DateTime? DumpJwtPayload(string? token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return null;

                var parts = token.Split('.');
                if (parts.Length < 2)
                    return null;

                // 🔍 Decode the header, not the payload
                string header = parts[0];
                header = header.PadRight(header.Length + (4 - header.Length % 4) % 4, '=');
                byte[] headerBytes = Convert.FromBase64String(header.Replace('-', '+').Replace('_', '/'));
                string headerJson = Encoding.UTF8.GetString(headerBytes);

                // LogToUI("📦 JWT Header:\n" + headerJson + "\n");

                JObject decodedHeader = JObject.Parse(headerJson);
                if (decodedHeader.ContainsKey("Expiration"))
                {
                    string expirationStr = decodedHeader["Expiration"]?.ToString();
                    if (DateTime.TryParse(expirationStr, out var expDate))
                    {
                        LogToUI($"🕓 Token Expiration (from header): {expDate:u} ({(expDate - DateTime.UtcNow).TotalMinutes:F1} mins from now)\n");
                        return expDate;
                    }
                }
                else
                {
                    LogToUI("⚠️ No 'Expiration' field found in JWT header.\n");
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error decoding JWT header: {ex.Message}\n");
            }

            return null;
        }


        private void UpdateAuthStatusTimer(TimeSpan remaining)
        {
            _authStatusText.Text = $"🟢 Authenticated – {remaining.Minutes}m {remaining.Seconds}s remaining";
        }



        private List<string> GetSelectedTargetValues()
        {
            return _fqdnManagerSelectedItems
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private List<ManagementGroup> ResolveSelectedManagementGroups()
        {
            var results = new List<ManagementGroup>();

            try
            {
                var groups = (_managementGroupComboBox?.ItemsSource as IEnumerable)?.Cast<object>().OfType<ManagementGroup>().ToList()
                    ?? _managementGroupComboBox?.Items?.Cast<object>().OfType<ManagementGroup>().ToList()
                    ?? new List<ManagementGroup>();

                foreach (var selectedName in GetSelectedTargetValues())
                {
                    var mg = groups.FirstOrDefault(g =>
                        string.Equals((g.Name ?? string.Empty).Trim(), selectedName, StringComparison.OrdinalIgnoreCase));

                    if (mg != null)
                        results.Add(mg);
                }

                if (results.Count == 0 && _managementGroupComboBox?.SelectedItem is ManagementGroup selectedMg)
                    results.Add(selectedMg);
                else if (results.Count == 0 && _managementGroupComboBox?.SelectedItem is string selectedNameString && !string.IsNullOrWhiteSpace(selectedNameString))
                {
                    var mg = groups.FirstOrDefault(g =>
                        string.Equals((g.Name ?? string.Empty).Trim(), selectedNameString.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (mg != null)
                        results.Add(mg);
                }
            }
            catch
            {
            }

            return results
                .GroupBy(x => $"{x.Name}|{x.UsableId}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private JObject? BuildManagementGroupScopeOrNull(List<ManagementGroup> groups)
        {
            if (groups == null || groups.Count == 0)
                return null;

            if (groups.Count == 1)
            {
                var mg = groups[0];

                if (mg.UsableId <= 0)
                {
                    if (_authService.IsMultiTenant)
                    {
                        return JObject.FromObject(new
                        {
                            Attribute = "managementgroup",
                            Operator = "==",
                            Value = "global"
                        });
                    }

                    return new JObject();
                }

                return JObject.FromObject(new
                {
                    Attribute = "managementgroup",
                    Operator = "==",
                    Value = mg.UsableId.ToString()
                });
            }

            var usableGroups = groups.Where(g => g != null && g.UsableId > 0).ToList();
            if (usableGroups.Count == 0)
            {
                if (_authService.IsMultiTenant)
                {
                    return JObject.FromObject(new
                    {
                        Attribute = "managementgroup",
                        Operator = "==",
                        Value = "global"
                    });
                }

                return new JObject();
            }

            var orArray = new JArray();
            foreach (var mg in usableGroups)
            {
                orArray.Add(JObject.FromObject(new
                {
                    Attribute = "managementgroup",
                    Operator = "==",
                    Value = mg.UsableId.ToString()
                }));
            }

            return new JObject
            {
                ["Operator"] = "or",
                ["Value"] = orArray
            };
        }

        private ManagementGroup? ResolveSelectedManagementGroup()
        {
            return ResolveSelectedManagementGroups().FirstOrDefault();
        }

        private void SyncManagementGroupComboFromSelection()
        {
            try
            {
                if (!string.Equals(GetSelectedTargetingMode(), "Management Group", StringComparison.OrdinalIgnoreCase))
                    return;

                if (_managementGroupComboBox == null)
                    return;

                var selected = ResolveSelectedManagementGroup();
                if (selected != null)
                    _managementGroupComboBox.SelectedItem = selected;
            }
            catch
            {
            }
        }

        private async Task LoadManagementGroupsAsync()
        {
            if (_selectedPlatform == null || string.IsNullOrEmpty(_token))
            {
                LogToUI("⚠️ Platform or token missing — can't load MG list.\n");
                return;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                string endpoint = "Consumer/ManagementGroups";

                var json = await ApiLogger.LogApiCallAsync(
                    label: "LoadManagementGroups",
                    endpoint: endpoint,
                    apiCall: async () =>
                    {
                        var response = await client.GetAsync($"https://{_selectedPlatform.Url}/{endpoint}");
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync();
                    },
                    payloadJson: "" // Not a POST, so no body to log
                );

                var array = JArray.Parse(json);
                var groups = array.Select(g => new ManagementGroup
                {
                    Name = g["Name"]?.ToString() ?? "(Unknown)",
                    UsableId = g["UsableId"]?.ToObject<int>() ?? 0
                }).ToList();

                // Optional: include a synthetic "All Devices" entry in the MG dropdown (no server-side MG constraint)
                try
                {
                    bool allowAll = false;
                    try
                    {
                        var cfg = _configHelper?.GetConfiguration();
                        if (cfg != null)
                            bool.TryParse(cfg["AllowAllDevicesInManagementGroupDropdown"], out allowAll);
                    }
                    catch { }

                    if (allowAll)
                    {
                        // Use UsableId = -1 to distinguish from real MGs.
                        if (!groups.Any(g => string.Equals(g.Name, "All Devices", StringComparison.OrdinalIgnoreCase)))
                            groups.Insert(0, new ManagementGroup { Name = "All Devices", UsableId = -1 });
                    }
                }
                catch { }

                _managementGroupComboBox.ItemsSource = groups;

                var defaultGroup = groups.FirstOrDefault(g => g.Name == _defaultMG);
                if (defaultGroup != null)
                {
                    _managementGroupComboBox.SelectedItem = defaultGroup;
                }

                LogToUI($"📋 Loaded {groups.Count} management groups.\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load management groups: {ex.Message}\n");
            }
        }

        private void SortExperienceResults(string column, bool ascending)
        {
            if (string.IsNullOrWhiteSpace(column) || _filteredExperienceResults == null || !_filteredExperienceResults.Any())
                return;

            // 🔐 Verify the column exists in at least one row
            if (!_filteredExperienceResults[0].ContainsKey(column))
            {
                LogToUI($"❌ Cannot sort: Column '{column}' not found in experience results.\n");
                return;
            }

            try
            {
                _filteredExperienceResults = ascending
                    ? _filteredExperienceResults.OrderBy(row =>
                        row.TryGetValue(column, out var val) && val != null ? val.ToString() : "").ToList()
                    : _filteredExperienceResults.OrderByDescending(row =>
                        row.TryGetValue(column, out var val) && val != null ? val.ToString() : "").ToList();
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Sorting error for '{column}': {ex.Message}\n");
            }
        }



        private IBrush GetBrushForScore(string header, string value)
        {
            // Don't color non-score fields like FQDN
            if (header.Equals("Fqdn", StringComparison.OrdinalIgnoreCase) || !value.All(char.IsDigit))
            {
                return (IBrush)(Application.Current.Resources.TryGetResource("ThemeForegroundBrush", Application.Current.ActualThemeVariant, out var brush)
                    ? brush
                    : Brushes.White);
            }

            if (!int.TryParse(value, out int score))
                return Brushes.Gray;

            if (score >= 90)
                return Brushes.LimeGreen;
            if (score >= 70)
                return Brushes.Goldenrod;
            if (score >= 50)
                return Brushes.Orange;

            return Brushes.Red;
        }

        /*
        private void ApplyExperienceSort_Click(object sender, RoutedEventArgs e)
        {
            LogToUI("🧪 ApplyExperienceSort_Click not yet implemented.");
        }
        private void PopulateMeasuresListBox()
        {
            if (MeasureCheckboxList == null)
            {
                LogToUI("⚠️ MeasureCheckboxList is null, cannot populate.");
                return;
            }

            if (_experienceMeasures == null || _experienceMeasures.Count == 0)
            {
                LogToUI("⚠️ No experience measures available to populate.");
                return;
            }

            LogToUI($"📋 Preparing to populate measure checkboxes with {_experienceMeasures.Count} measures...");

            var items = _experienceMeasures
                .Where(m => !m.Hidden)
                .OrderBy(m => m.Title)
                .Select(m => new SelectableMeasure
                {
                    Measure = m,
                    IsSelected = _selectedMeasureTitles.Contains(m.Title)
                })
                .ToList();

            MeasureCheckboxList.ItemsSource = items;

            LogToUI($"✅ Measure checkbox list populated with {items.Count} items.");
        }

        private void OpenFilterPanel_Click(object? sender, RoutedEventArgs e)
        {
            var popup = this.FindControl<Popup>("FilterPopup");
            if (popup != null)
                popup.IsOpen = !popup.IsOpen;
        }

        private void OpenMeasurePanel_Click(object? sender, RoutedEventArgs e)
        {
            var popup = this.FindControl<Popup>("MeasurePopup");
            if (popup != null)
                popup.IsOpen = !popup.IsOpen;
        }

        */
        private Dictionary<string, List<string>> GetSelectedExperienceFilters()
        {
            var selectedFilters = new Dictionary<string, List<string>>();

            selectedFilters["ManagementGroup"] = GetSelectedListBoxValues("ExperienceMgmtGroupListBox");
            selectedFilters["Location"] = GetSelectedListBoxValues("ExperienceLocationListBox");
            selectedFilters["DeviceModel"] = GetSelectedListBoxValues("ExperienceModelListBox");
            selectedFilters["OperatingSystem"] = GetSelectedListBoxValues("ExperienceOsListBox");
            selectedFilters["Criticality"] = GetSelectedListBoxValues("ExperienceCriticalityListBox");

            return selectedFilters;
        }


        private List<string> GetSelectedListBoxValues(string listBoxName)
        {
            var listBox = this.FindControl<ListBox>(listBoxName);
            var selected = new List<string>();

            if (listBox != null)
            {
                foreach (var item in listBox.SelectedItems)
                {
                    if (item is string str)
                    {
                        selected.Add(str);
                        LogToUI($"✔️ {listBoxName} selected: {str}\n");
                    }
                }

                LogToUI($"🔎 {listBoxName} — Total selected: {selected.Count}\n");
            }
            else
            {
                LogToUI($"⚠️ Could not find ListBox: {listBoxName}\n");
            }

            return selected;
        }



        private void DebugSelectedFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedExperienceFilters();
            foreach (var kvp in selected)
            {
                LogToUI($"🔍 Filter: {kvp.Key} — Selected: {kvp.Value.Count}\n");
                foreach (var val in kvp.Value)
                {
                    LogToUI($"    • {val}\n");
                }
            }
        }

        private enum DeviceExplorerStatusFilter
        {
            All = 0,
            Online = 1,
            Offline = 2
        }

        private sealed class DeviceSearchResultItem
        {
            public string Fqdn { get; set; } = string.Empty;
            public string PrimaryUser { get; set; } = string.Empty;
            public bool IsOnline { get; set; }
            public IBrush StatusBrush => IsOnline ? Brushes.LimeGreen : Brushes.IndianRed;
            public override string ToString() => Fqdn;
        }

        private sealed class FqdnManagerResultItem
        {
            public string Fqdn { get; set; } = string.Empty;
            public string User { get; set; } = string.Empty;
            public bool IsOnline { get; set; }
        }

        private string _lastDeviceSearchRaw = string.Empty;

        private DeviceExplorerStatusFilter GetDeviceExplorerStatusFilter()
        {
            // Works even if the visual tree isn't ready yet.
            var combo = _deviceStatusFilterCombo;
            if (combo == null)
                return _deviceExplorerStatusFilter;

            _deviceExplorerStatusFilter = combo.SelectedIndex switch
            {
                1 => DeviceExplorerStatusFilter.Online,
                2 => DeviceExplorerStatusFilter.Offline,
                _ => DeviceExplorerStatusFilter.All
            };

            return _deviceExplorerStatusFilter;
        }

        private async Task UpdateDeviceSearchResultsAsync(string raw)
        {
            _lastDeviceSearchRaw = raw ?? string.Empty;

            var normalized = NormalizeWildcardSearch(raw ?? string.Empty);
            var statusFilter = GetDeviceExplorerStatusFilter();

            // Empty / "all" / "*" means: browse first page of devices (no filter)
            if (normalized == null)
            {
                LogToUI("🔍 Browsing devices (no FQDN filter).");

                var bodyAll = new
                {
                    Filter = (_resultsServerFilterAllCheckBox?.IsChecked == true) ? BuildResultsServerFilterPayload() : null,
                    Sort = new[]
                    {
                        new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" }
                    },
                    Start = 1,
                    PageSize = 25
                };

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform?.Consumer ?? "Explorer");

                var url = $"https://{_selectedPlatform?.Url}/consumer/Devices";

                try
                {
                    var response = await client.PostAsync(url,
                        new StringContent(JsonConvert.SerializeObject(bodyAll), Encoding.UTF8, "application/json"));

                    var json = await response.Content.ReadAsStringAsync();

                    var result = JsonConvert.DeserializeObject<JObject>(json);
                    var resultsArray = result?["Items"] as JArray;

                    var listBox = this.FindControl<ListBox>("DeviceSearchResults");

                    if (resultsArray != null && resultsArray.Count > 0)
                    {
                        var items = resultsArray
                            .Select(device =>
                            {
                                var fqdn = device["Fqdn"]?.ToString() ?? "(unknown)";
                                var isOnline = device["Status"]?.ToString() == "1";
                                return new DeviceSearchResultItem { Fqdn = fqdn, IsOnline = isOnline };
                            })
                            .Where(item => statusFilter == DeviceExplorerStatusFilter.All ||
                                           (statusFilter == DeviceExplorerStatusFilter.Online && item.IsOnline) ||
                                           (statusFilter == DeviceExplorerStatusFilter.Offline && !item.IsOnline))
                            .ToList();

                        listBox.ItemsSource = items;
                        LogToUI($"✅ {items.Count} devices loaded.");
                    }
                    else
                    {
                        listBox.ItemsSource = new List<DeviceSearchResultItem>();
                        LogToUI("⚠️ No devices returned.");
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"❌ Device browse failed: {ex.Message}", "error");
                }

                return;
            }

            // Normal "like" search
            var query = normalized.Trim();
            if (query.Length < 3)
                return;

            LogToUI($"🔍 Searching for FQDN like: {query}");

            var body = new
            {
                Filter = new { Attribute = "Fqdn", Operator = "like", Value = $"%{query}%" },
                Sort = new[]
                {
                    new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" }
                },
                Start = 1,
                PageSize = 25
            };

            using var client2 = new HttpClient();
            client2.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client2.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform?.Consumer ?? "Explorer");

            var url2 = $"https://{_selectedPlatform.Url}/consumer/Devices";

            try
            {
                var response = await client2.PostAsync(url2,
                    new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<JObject>(json);
                var resultsArray = result?["Items"] as JArray;

                var listBox = this.FindControl<ListBox>("DeviceSearchResults");

                if (resultsArray != null && resultsArray.Count > 0)
                {
                    var items = resultsArray
                        .Select(device =>
                        {
                            var fqdn = device["Fqdn"]?.ToString() ?? "(unknown)";
                            var isOnline = device["Status"]?.ToString() == "1";
                            return new DeviceSearchResultItem { Fqdn = fqdn, IsOnline = isOnline };
                        })
                        .Where(item => statusFilter == DeviceExplorerStatusFilter.All ||
                                       (statusFilter == DeviceExplorerStatusFilter.Online && item.IsOnline) ||
                                       (statusFilter == DeviceExplorerStatusFilter.Offline && !item.IsOnline))
                        .ToList();

                    listBox.ItemsSource = items;
                    LogToUI($"✅ {items.Count} devices loaded.");
                }
                else
                {
                    listBox.ItemsSource = new List<DeviceSearchResultItem>();
                    LogToUI("⚠️ No matches found.");
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Device search failed: {ex.Message}", "error");
            }
        }

        private void HideInstructionFqdnPicker()
        {
            try
            {
                if (_instructionFqdnSearchResultsListBox != null)
                {
                    _instructionFqdnSearchResultsListBox.SelectedItem = null;
                    _instructionFqdnSearchResultsListBox.SelectedItems?.Clear();
                }

                // Do not hide or clear the shared lower picker area.
                if (_instructionFqdnSearchResultsBorder != null)
                    _instructionFqdnSearchResultsBorder.IsVisible = true;
            }
            catch
            {
            }
        }

        private string _lastSelectedFqdnFromPicker = string.Empty;
        private DateTime _lastPickerSelectionUtc = DateTime.MinValue;





        private async Task UpdateInstructionFqdnSearchResultsAsync(string raw)
        {
            if (_instructionFqdnSearchResultsListBox == null || _instructionFqdnSearchResultsBorder == null)
                return;

            raw ??= string.Empty;

            bool searchTextChanged = !string.Equals(_lastInstructionFqdnSearchRaw, raw, StringComparison.Ordinal);
            if (searchTextChanged)
                _instructionFqdnSearchStart = 1;

            _lastInstructionFqdnSearchRaw = raw;

            var targetingMode = GetSelectedTargetingMode();
            var isMgmtGroup = string.Equals(targetingMode, "Management Group", StringComparison.OrdinalIgnoreCase);

            try
            {
                _instructionFqdnSearchCts?.Cancel();
                _instructionFqdnSearchCts?.Dispose();
            }
            catch { }

            _instructionFqdnSearchCts = new CancellationTokenSource();
            var ct = _instructionFqdnSearchCts.Token;

            try
            {
                await Task.Delay(150, ct);
            }
            catch
            {
                return;
            }

            if (ct.IsCancellationRequested)
                return;

            // =============================
            // MANAGEMENT GROUP SEARCH
            // =============================
            if (isMgmtGroup)
            {
                var groups =
                    (_managementGroupComboBox?.ItemsSource as IEnumerable)?.Cast<object>().OfType<ManagementGroup>().ToList()
                    ?? _managementGroupComboBox?.Items?.Cast<object>().OfType<ManagementGroup>().ToList()
                    ?? new List<ManagementGroup>();

                var trimmed = raw.Trim();

                var selected = new HashSet<string>(
                    _fqdnManagerSelectedItems
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                IEnumerable<ManagementGroup> filtered = groups;

                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    filtered = filtered.Where(g =>
                        (g.Name ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase));
                }

                var items = filtered
                    .Where(g => !selected.Contains(g.Name ?? string.Empty))
                    .OrderBy(g => g.Name)
                    .Take(250)
                    .Select(g => new DeviceSearchResultItem
                    {
                        Fqdn = g.Name ?? string.Empty,
                        PrimaryUser = "",
                        IsOnline = true
                    })
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _instructionFqdnSearchResultsListBox.ItemsSource = items;
                    _instructionFqdnSearchResultsBorder.IsVisible = true;
                    _instructionFqdnSearchLastCount = items.Count;

                    var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                    var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                    var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                    if (summaryText != null)
                        summaryText.Text = $"Showing {items.Count} management group result(s)";

                    if (prevButton != null)
                        prevButton.IsEnabled = false;

                    if (nextButton != null)
                        nextButton.IsEnabled = false;
                });

                return;
            }

            // =============================
            // DEVICE SEARCH (FQDN / USER)
            // =============================

            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_selectedPlatform.Url) || string.IsNullOrWhiteSpace(_token))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _instructionFqdnSearchResultsListBox.ItemsSource = new List<DeviceSearchResultItem>();
                    _instructionFqdnSearchResultsBorder.IsVisible = true;
                    _instructionFqdnSearchLastCount = 0;

                    var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                    var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                    var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                    if (summaryText != null)
                        summaryText.Text = "Showing 0 results";

                    if (prevButton != null)
                        prevButton.IsEnabled = false;

                    if (nextButton != null)
                        nextButton.IsEnabled = false;
                });
                return;
            }

            var trimmedDevice = raw.Trim();

            bool browseMode =
                string.IsNullOrWhiteSpace(raw) ||
                trimmedDevice.Length == 0 ||
                string.Equals(trimmedDevice, "all", StringComparison.OrdinalIgnoreCase) ||
                trimmedDevice == "*" ||
                trimmedDevice == "*.*" ||
                trimmedDevice == "%";

            bool searchByUser = TryFindControl<RadioButton>("SearchByPrimaryUserRadioButton")?.IsChecked == true;
            string attribute = searchByUser ? "User" : "Fqdn";

            object? filterPayload = null;

            if (!browseMode)
            {
                if (trimmedDevice.Length < 2)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _instructionFqdnSearchResultsListBox.ItemsSource = new List<DeviceSearchResultItem>();
                        _instructionFqdnSearchResultsBorder.IsVisible = true;
                        _instructionFqdnSearchLastCount = 0;

                        var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                        var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                        var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                        if (summaryText != null)
                            summaryText.Text = "Showing 0 results";

                        if (prevButton != null)
                            prevButton.IsEnabled = false;

                        if (nextButton != null)
                            nextButton.IsEnabled = false;
                    });
                    return;
                }

                filterPayload = new
                {
                    Attribute = attribute,
                    Operator = "like",
                    Value = $"%{trimmedDevice.Replace("*", "%")}%"
                };
            }

            var body = new
            {
                Filter = filterPayload,
                Sort = new[]
                {
            new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" }
        },
                Start = _instructionFqdnSearchStart,
                PageSize = _instructionFqdnSearchPageSize
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform.Consumer ?? "Explorer");

            var url = $"https://{_selectedPlatform.Url}/consumer/Devices";

            try
            {
                var response = await client.PostAsync(
                    url,
                    new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"),
                    ct);

                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _instructionFqdnSearchResultsListBox.ItemsSource = new List<DeviceSearchResultItem>();
                        _instructionFqdnSearchResultsBorder.IsVisible = true;
                        _instructionFqdnSearchLastCount = 0;

                        var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                        var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                        var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                        if (summaryText != null)
                            summaryText.Text = "Showing 0 results";

                        if (prevButton != null)
                            prevButton.IsEnabled = false;

                        if (nextButton != null)
                            nextButton.IsEnabled = false;
                    });

                    LogToUI($"❌ Instruction FQDN search failed: {(int)response.StatusCode} {response.ReasonPhrase}", "error");
                    return;
                }

                var result = JsonConvert.DeserializeObject<JObject>(json);
                var resultsArray = result?["Items"] as JArray;

                var items = resultsArray?
                    .Select(device => new DeviceSearchResultItem
                    {
                        Fqdn = (device?["Fqdn"]?.ToString() ?? string.Empty).Trim(),
                        PrimaryUser = (device?["User"]?.ToString() ?? string.Empty).Trim(),
                        IsOnline = (device?["Status"]?.ToString() ?? string.Empty) == "1"
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Fqdn))
                    .Where(x => !_fqdnManagerSelectedItems.Any(sel =>
                        string.Equals(sel, x.Fqdn, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(x => x.Fqdn, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList()
                    ?? new List<DeviceSearchResultItem>();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _instructionFqdnSearchResultsListBox.ItemsSource = items;
                    _instructionFqdnSearchResultsBorder.IsVisible = true;
                    _instructionFqdnSearchLastCount = items.Count;

                    var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                    var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                    var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                    int from = items.Count == 0 ? 0 : _instructionFqdnSearchStart;
                    int to = items.Count == 0 ? 0 : _instructionFqdnSearchStart + items.Count - 1;
                    int totalCount = result?["TotalCount"]?.Value<int>() ?? 0;

                    if (summaryText != null)
                    {
                        if (totalCount > 0)
                        {
                            if (browseMode)
                            {
                                summaryText.Text = items.Count == 0
                                    ? $"Showing 0 of {totalCount} devices"
                                    : $"Showing {from}-{to} of {totalCount} devices";
                            }
                            else
                            {
                                summaryText.Text = items.Count == 0
                                    ? $"Showing 0 of {totalCount} matching results"
                                    : $"Showing {from}-{to} of {totalCount} matching results";
                            }
                        }
                        else
                        {
                            if (browseMode)
                            {
                                summaryText.Text = items.Count == 0
                                    ? "Showing 0 devices"
                                    : $"Showing {from}-{to} devices";
                            }
                            else
                            {
                                summaryText.Text = items.Count == 0
                                    ? "Showing 0 matching results"
                                    : $"Showing {from}-{to} matching results";
                            }
                        }
                    }

                    if (prevButton != null)
                        prevButton.IsEnabled = _instructionFqdnSearchStart > 1;

                    if (nextButton != null)
                        nextButton.IsEnabled = to < totalCount;
                });

                if (browseMode)
                    LogToUI($"✅ Loaded {items.Count} devices from page starting at {_instructionFqdnSearchStart}.");
                else
                    LogToUI($"✅ Loaded {items.Count} matching device(s) from page starting at {_instructionFqdnSearchStart}.");
            }
            catch (OperationCanceledException)
            {
                // ignore debounce cancellation
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _instructionFqdnSearchResultsListBox.ItemsSource = new List<DeviceSearchResultItem>();
                        _instructionFqdnSearchResultsBorder.IsVisible = true;
                        _instructionFqdnSearchLastCount = 0;

                        var summaryText = this.FindControl<TextBlock>("InstructionFqdnSearchSummaryText");
                        var prevButton = this.FindControl<Button>("InstructionFqdnPrevButton");
                        var nextButton = this.FindControl<Button>("InstructionFqdnNextButton");

                        if (summaryText != null)
                            summaryText.Text = "Showing 0 results";

                        if (prevButton != null)
                            prevButton.IsEnabled = false;

                        if (nextButton != null)
                            nextButton.IsEnabled = false;
                    });

                    LogToUI($"❌ Instruction FQDN search failed: {ex.Message}", "error");
                }
            }
        }

        private async void OnInstructionFqdnPrevClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_instructionFqdnSearchStart <= 1)
                return;

            _instructionFqdnSearchStart = Math.Max(1, _instructionFqdnSearchStart - _instructionFqdnSearchPageSize);
            await UpdateInstructionFqdnSearchResultsAsync(_lastInstructionFqdnSearchRaw);
        }

        private async void OnInstructionFqdnNextClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_instructionFqdnSearchLastCount < _instructionFqdnSearchPageSize)
                return;

            _instructionFqdnSearchStart += _instructionFqdnSearchPageSize;
            await UpdateInstructionFqdnSearchResultsAsync(_lastInstructionFqdnSearchRaw);
        }


        private bool _isUpdatingInstructionFqdnSelection;

        private async void InstructionFqdnSearchResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingInstructionFqdnSelection)
                return;

            try
            {
                if (_instructionFqdnSearchResultsListBox == null)
                    return;

                _isUpdatingInstructionFqdnSelection = true;

                var selectedItems = _instructionFqdnSearchResultsListBox.SelectedItems?
                    .Cast<object>()
                    .OfType<DeviceSearchResultItem>()
                    .ToList() ?? new List<DeviceSearchResultItem>();

                if (selectedItems.Count == 0 && _instructionFqdnSearchResultsListBox.SelectedItem is DeviceSearchResultItem one)
                    selectedItems.Add(one);

                if (selectedItems.Count == 0)
                    return;

                foreach (var selected in selectedItems)
                {
                    var value = (selected.Fqdn ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (_fqdnManagerSelectedItems.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (_fqdnManagerSelectedItems.Count >= 10)
                    {
                        await ShowSimpleDialogAsync(string.Equals(GetSelectedTargetingMode(), "Management Group", StringComparison.OrdinalIgnoreCase) ? "Management Groups" : "FQDN List", "Maximum of 10 selections allowed");
                        break;
                    }

                    _fqdnManagerSelectedItems.Add(value);
                }

                UpdateFqdnManagerSelectedHeader();
                SyncManagementGroupComboFromSelection();
                _instructionFqdnSearchResultsListBox.SelectedItem = null;
                _instructionFqdnSearchResultsListBox.SelectedItems?.Clear();
                await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty);
                await UpdatePreviewAsync();
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed processing target selection: {ex.Message}\n");
            }
            finally
            {
                _isUpdatingInstructionFqdnSelection = false;
            }
        }
        private async void DeviceSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            await UpdateDeviceSearchResultsAsync(box?.Text ?? string.Empty);
        }

        private async void DeviceStatusFilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                _deviceStatusFilterCombo = combo;

                _deviceExplorerStatusFilter = combo.SelectedIndex switch
                {
                    1 => DeviceExplorerStatusFilter.Online,
                    2 => DeviceExplorerStatusFilter.Offline,
                    _ => DeviceExplorerStatusFilter.All
                };
            }

            await UpdateDeviceSearchResultsAsync(_lastDeviceSearchRaw);
        }


        private async void DeviceSearchResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;

            var item = listBox?.SelectedItem as DeviceSearchResultItem;
            var fqdn = item?.Fqdn ?? listBox?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(fqdn))
                return;

            var device = await GetDeviceSummaryAsync(fqdn);
            if (device == null)
                return;

            var detailPanel = this.FindControl<ContentControl>("DeviceExplorerDetailPanel");
            detailPanel.Content = BuildDeviceDetailPanel(device);  // ← fixed line
        }


        private async void OnDeviceSearchChanged(object? sender, EventArgs e)
        {
            var box = sender as TextBox;
            string query = box?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                return;

            var body = new
            {
                Filter = new { Attribute = "Fqdn", Operator = "like", Value = $"%{query}%" },
                Sort = new[] {
            new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" }
        },
                Start = 1,
                PageSize = 25
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform.Consumer ?? "Explorer");

            var response = await client.PostAsync(
                $"https://{_selectedPlatform.Url}/consumer/Devices",
                new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<JObject>(json);
            var resultsArray = result?["Devices"] as JArray;

            var listBox = this.FindControl<ListBox>("DeviceSearchResults");

            if (resultsArray != null)
            {
                var items = resultsArray
                    .Select(device =>
                    {
                        string fqdn = device["Fqdn"]?.ToString() ?? "(unknown)";
                        string status = device["Status"]?.ToString() == "1" ? "Online" : "Offline";
                        return $"{fqdn} ({status})";
                    })
                    .ToList();

                listBox.ItemsSource = items;
            }
        }

        private async void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            string selected = listBox?.SelectedItem?.ToString();
            string fqdn = selected?.Split(" (")[0]; // Trim off " (Online)" suffix

            var device = await GetDeviceSummaryAsync(fqdn); // Reuse your working method

            var panel = this.FindControl<ContentControl>("DeviceExplorerDetailPanel");
            panel.Content = BuildDeviceDetailPanel(device); // You already have a method like this
        }
        private StackPanel BuildDeviceDetailPanel(DeviceTowerModel device)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 16,
                Margin = new Thickness(10)
            };

            void AddGroupHeader(string icon, string title)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{icon} {title}",
                    FontWeight = FontWeight.Bold,
                    FontSize = 16,
                    Margin = new Thickness(0, 10, 0, 5)
                });
            }

            void AddRow(Grid grid, int row, string label, string value, bool highlightStatus = false)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                var labelText = new TextBlock
                {
                    Text = $"{label}:",
                    FontWeight = FontWeight.Bold
                };
                Grid.SetRow(labelText, row);
                Grid.SetColumn(labelText, 0);
                grid.Children.Add(labelText);

                if (highlightStatus)
                {
                    var statusStack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6
                    };

                    var statusDot = new Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = value == "Online" ? Brushes.LimeGreen : Brushes.Red,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var statusText = new TextBlock
                    {
                        Text = value,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    statusStack.Children.Add(statusDot);
                    statusStack.Children.Add(statusText);
                    Grid.SetRow(statusStack, row);
                    Grid.SetColumn(statusStack, 1);
                    grid.Children.Add(statusStack);
                }
                else
                {
                    var valueText = new TextBlock
                    {
                        Text = value ?? "(empty)"
                    };
                    Grid.SetRow(valueText, row);
                    Grid.SetColumn(valueText, 1);
                    grid.Children.Add(valueText);
                }
            }

            Grid CreateDetailGrid()
            {
                return new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
                    RowDefinitions = new RowDefinitions()
                };
            }

            // Identity Section
            AddGroupHeader("🪪", "Identity");
            var identityGrid = CreateDetailGrid();
            AddRow(identityGrid, 0, "FQDN", device.Fqdn);
            AddRow(identityGrid, 1, "Status", device.Status == 1 ? "Online" : "Offline", highlightStatus: true);
            AddRow(identityGrid, 2, "Serial Number", device.SerialNumber);
            AddRow(identityGrid, 3, "User", device.User);
            AddRow(identityGrid, 4, "OU Path", device.OuPath);
            panel.Children.Add(identityGrid);

            // Hardware Section
            AddGroupHeader("💻", "Hardware");
            var hardwareGrid = CreateDetailGrid();
            AddRow(hardwareGrid, 0, "OS", device.OsVerTxt);
            AddRow(hardwareGrid, 1, "CPU", device.CpuType);
            AddRow(hardwareGrid, 2, "Architecture", device.OsArchitecture);
            AddRow(hardwareGrid, 3, "RAM (MB)", device.RamMB.ToString());
            AddRow(hardwareGrid, 4, "BIOS Version", device.BiosVersion);
            AddRow(hardwareGrid, 5, "Encryption", device.NativeDiskEncryption);
            panel.Children.Add(hardwareGrid);

            // Network Section
            AddGroupHeader("🌐", "Network");
            var networkGrid = CreateDetailGrid();
            AddRow(networkGrid, 0, "IP", device.LocalIpAddress);
            AddRow(networkGrid, 1, "MAC", device.MAC);
            AddRow(networkGrid, 2, "Connecting IP", device.ConnectingIpAddress);
            AddRow(networkGrid, 3, "Gateway", device.DefaultGateway);
            AddRow(networkGrid, 4, "DNS", device.PrimaryDnsServer);
            AddRow(networkGrid, 5, "Secondary DNS", device.SecondaryDnsServers);
            AddRow(networkGrid, 6, "Disk Free MB", device.FreeOsDiskSpaceMb.ToString());
            panel.Children.Add(networkGrid);

            // Operating System
            AddGroupHeader("🧩", "Operating System");
            var osGrid = CreateDetailGrid();
            AddRow(osGrid, 0, "OS Locale", device.OsLocale);
            AddRow(osGrid, 1, "Install Date", device.OsInstallUtc.ToString("g"));
            AddRow(osGrid, 2, "Last Boot", device.LastBootUTC.ToString("g"));
            AddRow(osGrid, 3, "Last Connection", device.LastConnUtc.ToString("g"));
            AddRow(osGrid, 4, "Created", device.CreatedUtc.ToString("g"));
            AddRow(osGrid, 5, "Connection State", string.Join(", ", device.ConnectionState ?? new List<string>()));
            AddRow(osGrid, 6, "Cert Type", device.CertType);
            AddRow(osGrid, 7, "Cert Expiry", device.CertExpiryUtc?.ToString("g") ?? "N/A");
            AddRow(osGrid, 8, "Primary Connection Type", device.PrimaryConnectionType);
            panel.Children.Add(osGrid);

            // Grouping
            AddGroupHeader("🏷", "Grouping");
            var tagGrid = CreateDetailGrid();
            AddRow(tagGrid, 0, "Location", device.Location);
            AddRow(tagGrid, 1, "Time Zone", device.TimeZone.ToString());
            AddRow(tagGrid, 2, "Time Zone ID", device.TimeZoneId);
            AddRow(tagGrid, 3, "Model", device.Model);
            AddRow(tagGrid, 4, "Domain", device.Domain);
            AddRow(tagGrid, 5, "Device Type", device.DeviceType);
            AddRow(tagGrid, 6, "Chassis Type", device.ChassisType.ToString());
            AddRow(tagGrid, 7, "Manufacturer", device.Manufacturer);
            AddRow(tagGrid, 8, "VR Platform", device.VrPlatform);
            panel.Children.Add(tagGrid);

            if (device.CoverageTags != null && device.CoverageTags.Any())
            {
                AddGroupHeader("📌", "Coverage Tags");
                foreach (var tag in device.CoverageTags)
                {
                    var tagText = new TextBlock
                    {
                        Text = $"{tag.Key}: {tag.Value}",
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    panel.Children.Add(tagText);
                }
            }

            return panel;
        }





        private async Task<DeviceTowerModel?> GetDeviceSummaryAsync(string fqdn)
        {
            try
            {
                string encodedFqdn = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fqdn));
                string url = $"https://{_selectedPlatform.Url}/consumer/Devices/fqdn/{encodedFqdn}";

                LogToUI($"🌐 Calling: {url}\n");
                //LogToUI($"🔐 Using token (last 4): {_token?.TakeLast(4)} | Consumer: {_selectedPlatform.Consumer}\n");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform.Consumer ?? "Explorer");

                var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                // LogToUI($"📥 Raw JSON (first 300 chars): {json.Substring(0, Math.Min(300, json.Length))}...\n");

                if (!response.IsSuccessStatusCode)
                {
                    LogToUI($"❌ Failed to load device summary for {fqdn}: {response.StatusCode}");

                    // If we get Unauthorized, the token is no longer valid (regardless of the UI countdown).
                    // Immediately mark unauthenticated so the user is prompted to re-login.
                    if ((int)response.StatusCode == 401)
                    {
                        try
                        {
                            _authService?.Logout();
                        }
                        catch { }

                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                UpdateAuthStatusIndicator(false);
                            }
                            catch { }
                        });
                    }

                    return null;
                }

                var obj = JsonConvert.DeserializeObject<DeviceTowerModel>(json);
                LogToUI($"✅ Successfully deserialized device summary for {fqdn}\n");

                return obj;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Error loading device summary: {ex.Message}\n");
                return null;
            }
        }


        private async void ShowDevicePanel(string fqdn)
        {
            var model = await GetDeviceSummaryAsync(fqdn);
            if (model != null)
            {
                // LogToUI($"📋 Showing device panel for {fqdn}...");

                var detailWindow = new DeviceDetailWindow(model);
                detailWindow.ShowDialog(this); // Use Show(this) if you want it non-modal
            }
        }
        private void OnClearResultsClicked(object? sender, RoutedEventArgs e)
        {
            LogToUI("🧹 Clear Results button clicked.\n");
            _parsedResults.Clear();
            _rawResults?.Clear();
            _filteredResults?.Clear();
            _columnFilters.Clear();

            this.FindControl<StackPanel>("RawResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ChartResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("AggregatedResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ExperienceResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ResultsPanel")?.Children.Clear();
        }

        private async void OnRefreshResultsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastInstructionId <= 0)
                {
                    LogToUI("⚠️ No instruction selected. Run an instruction or pick one from History.\n");
                    return;
                }

                _resultsPageSize = GetResultsPageSize();
                await LoadInstructionResultsPageAsync(_lastInstructionId, _resultsCurrentRange, resetPanels: true);

                // Also refresh status + progress summary (not just the data grid)
                var idStr = !string.IsNullOrWhiteSpace(_currentInstructionId)
                    ? _currentInstructionId
                    : _lastInstructionId.ToString();

                if (!string.IsNullOrWhiteSpace(idStr))
                {
                    // Don't start a new poll cycle here; just refresh under the current one
                    var runId = _resultsStatusPollRunId;

                    _ = RefreshInstructionStatusAsync(idStr, runId);

                    if (int.TryParse(idStr, out var iid))
                    {
                        _ = ShowInstructionProgressAsync(iid, log: false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Refresh Results failed: {ex.Message}\n");
            }
        }

        private void ClearAllResultPanels()
        {
            LogToUI("🧹 Clear Results on Instruction Change.\n");
            _parsedResults.Clear();
            _rawResults?.Clear();
            _filteredResults?.Clear();
            _columnFilters.Clear();

            this.FindControl<StackPanel>("RawResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ChartResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("AggregatedResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ExperienceResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ResultsPanel")?.Children.Clear();
        }
        private void ResetApplicationState()
        {
            // Clear instruction metadata and list
            _instructionDefinitions.Clear();
            _instructionMap.Clear();
            _instructionListBox.ItemsSource = null;
            _instructionListBox.SelectedItem = null;
            _selectedInstruction = null;

            // Clear input fields
            _searchBox.Text = "";
            _fqdnTextBox.Text = "";
            _instructionTtlBox.Text = "";
            _responseTtlBox.Text = "";

            // Reset match mode and management group dropdowns
            if (_matchTypeComboBox != null)
                _matchTypeComboBox.SelectedIndex = 0;
            _managementGroupComboBox.SelectedIndex = -1;

            // Clear parameter controls
            _parameterInputs.Clear();
            _fqdnParameterInputs.Clear();
            TryFindControl<StackPanel>("ParametersPanel")?.Children.Clear();
            TryFindControl<StackPanel>("FqdnParametersPanel")?.Children.Clear();

            // Reset experience measure state
            _selectedMeasureTitles.Clear();
            TryFindControl<ListBox>("ExperienceMeasuresListBox")?.SelectedItems?.Clear();

            // Clear instruction history
            var historyGrid = this.FindControl<DataGrid>("InstructionHistoryGrid");
            if (historyGrid != null)
                historyGrid.ItemsSource = null;

            // Clear results and filter states
            _parsedResults.Clear();
            _rawResults?.Clear();
            _filteredResults?.Clear();
            _columnFilters.Clear();
            _lastInstructionId = 0;
            _lastInstructionDefinition = null;

            // Clear device explorer state
            this.FindControl<StackPanel>("ExperienceResultsPanel")?.Children.Clear();
            var deviceDetailPanel = this.FindControl<ContentControl>("DeviceExplorerDetailPanel");
            if (deviceDetailPanel != null)
                deviceDetailPanel.Content = null;
            this.FindControl<TextBox>("DeviceSearchBox")?.Clear();
            var deviceSearchResults = this.FindControl<ListBox>("DeviceSearchResults");
            if (deviceSearchResults != null)
                deviceSearchResults.ItemsSource = null;



            // Clear results panels
            this.FindControl<StackPanel>("RawResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ChartResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("AggregatedResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ExperienceResultsPanel")?.Children.Clear();
            this.FindControl<StackPanel>("ResultsPanel")?.Children.Clear();
        }






        private void ResultsChartDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb)
                return;

            _resultsChartLastSelectedIndex = cb.SelectedIndex >= 0 ? cb.SelectedIndex : null;

            try
            {
                if (cb.SelectedItem is ComboBoxItem cbi && cbi.Content is string s)
                    _resultsChartLastSelectedLabel = s;
                else if (cb.SelectedItem != null)
                    _resultsChartLastSelectedLabel = cb.SelectedItem.ToString();
            }
            catch
            {
                // best-effort
            }
        }

        private void OnSettingsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                var border = this.FindControl<Border>("SettingsFlyoutBorder");
                if (border == null)
                    return;

                border.IsVisible = !border.IsVisible;

                if (border.IsVisible)
                {
                    LoadSettingsFlyoutFromConfig();
                    LoadPlatformsIntoSettingsFlyout();
                }
            }
            catch
            {
                // ignore
            }
        }

        private void OnCloseSettingsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                var border = this.FindControl<Border>("SettingsFlyoutBorder");
                if (border != null)
                    border.IsVisible = false;
            }
            catch
            {
                // ignore
            }
        }


        private void UpdateFqdnManagerSelectedHeader()
        {
            if (_fqdnManagerSelectedHeaderText != null)
            {
                var isMgmtGroup = string.Equals(GetSelectedTargetingMode(), "Management Group", StringComparison.OrdinalIgnoreCase);
                _fqdnManagerSelectedHeaderText.Text = isMgmtGroup
                    ? $" Management Groups ({_fqdnManagerSelectedItems.Count} / 10)"
                    : $" FQDNs ({_fqdnManagerSelectedItems.Count} / 10)";
            }

            if (_fqdnManagerSelectedListBox != null)
            {
                _fqdnManagerSelectedListBox.ItemsSource = null;
                _fqdnManagerSelectedListBox.ItemsSource = _fqdnManagerSelectedItems.ToList();
            }

            // Do NOT auto-show or auto-hide the flyout here.
            // Visibility should be controlled only by explicit open/close actions.
        }
        private static List<string> ParseFqdnList(string? raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private async void OnOpenFqdnManagerClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_fqdnManagerBorder != null)
                    _fqdnManagerBorder.IsVisible = true;

                UpdateFqdnManagerSelectedHeader();

                if (_fqdnManagerSearchTextBox != null)
                {
                    _fqdnManagerSearchTextBox.Focus();
                    _fqdnManagerSearchTextBox.CaretIndex = _fqdnManagerSearchTextBox.Text?.Length ?? 0;
                }

                await Task.CompletedTask;
            }
            catch
            {
            }
        }
        private void OnCloseFqdnManagerClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_fqdnManagerBorder != null)
                    _fqdnManagerBorder.IsVisible = false;
            }
            catch
            {
            }
        }

        private async void OnFqdnManagerSearchClicked(object? sender, RoutedEventArgs e)
        {
            await RefreshFqdnManagerResultsAsync(_fqdnManagerSearchTextBox?.Text ?? string.Empty);
        }

        private async Task RefreshFqdnManagerResultsAsync(string raw)
        {
            if (_fqdnManagerResultsGrid == null || _selectedPlatform == null || string.IsNullOrWhiteSpace(_token))
                return;

            try
            {
                _fqdnManagerSearchCts?.Cancel();
                _fqdnManagerSearchCts?.Dispose();
            }
            catch { }

            _fqdnManagerSearchCts = new CancellationTokenSource();
            var ct = _fqdnManagerSearchCts.Token;
            try { await Task.Delay(250, ct); } catch { return; }
            if (ct.IsCancellationRequested)
                return;

            var trimmed = (raw ?? string.Empty).Trim();
            bool showAll = string.IsNullOrWhiteSpace(trimmed) || trimmed == "*" || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase);
            bool searchByUser = _fqdnManagerSearchByUserRadioButton?.IsChecked == true;
            string attribute = searchByUser ? "User" : "Fqdn";

            object body = showAll
                ? new { Sort = new[] { new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" } }, Start = 1, PageSize = 50 }
                : new { Filter = new { Attribute = attribute, Operator = "like", Value = $"%{trimmed.Replace("*", "%")}%", Type = "string" }, Sort = new[] { new { label = "Device name A-Z", Column = "Fqdn", Direction = "ASC" } }, Start = 1, PageSize = 50 };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _selectedPlatform.Consumer ?? "Explorer");

            try
            {
                var response = await client.PostAsync($"https://{_selectedPlatform.Url}/consumer/Devices", new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), ct);
                if (!response.IsSuccessStatusCode)
                {
                    if (_fqdnManagerStatusText != null)
                        _fqdnManagerStatusText.Text = "Search failed.";
                    InstructionFqdnSearchResults.ItemsSource = new List<FqdnManagerResultItem>();
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<JObject>(json);
                var items = (result?["Items"] as JArray)?.Select(device => new FqdnManagerResultItem
                {
                    Fqdn = (device?["Fqdn"]?.ToString() ?? string.Empty).Trim(),
                    User = (device?["User"]?.ToString() ?? string.Empty).Trim(),
                    IsOnline = (device?["Status"]?.ToString() ?? string.Empty) == "1"
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Fqdn))
                .Where(x => !_fqdnManagerSelectedItems.Contains(x.Fqdn, StringComparer.OrdinalIgnoreCase))
                .GroupBy(x => x.Fqdn, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(50)
                .ToList() ?? new List<FqdnManagerResultItem>();

                InstructionFqdnSearchResults.ItemsSource = items;
                if (_fqdnManagerStatusText != null)
                    _fqdnManagerStatusText.Text = $"{items.Count} result(s) loaded.";

            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    InstructionFqdnSearchResults.ItemsSource = new List<FqdnManagerResultItem>();
                    if (_fqdnManagerStatusText != null)
                        _fqdnManagerStatusText.Text = "Search failed.";
                }
            }
        }

        private async void OnAddSelectedFqdnManagerResultClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_instructionFqdnSearchResultsListBox == null)
                    return;

                var selectedItems = _instructionFqdnSearchResultsListBox.SelectedItems?
                    .Cast<object>()
                    .OfType<DeviceSearchResultItem>()
                    .ToList()
                    ?? new List<DeviceSearchResultItem>();

                if (selectedItems.Count == 0)
                {
                    if (_instructionFqdnSearchResultsListBox.SelectedItem is DeviceSearchResultItem singleSelected)
                        selectedItems.Add(singleSelected);
                }

                if (selectedItems.Count == 0)
                    return;

                foreach (var selected in selectedItems)
                {
                    var value = (selected.Fqdn ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (_fqdnManagerSelectedItems.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (_fqdnManagerSelectedItems.Count >= 10)
                    {
                        await ShowSimpleDialogAsync("Target List", "Maximum of 10 selections allowed");
                        break;
                    }

                    _fqdnManagerSelectedItems.Add(value);

                    var primaryUser = (selected.PrimaryUser ?? string.Empty).Trim();
                    _selectedTargetPrimaryUsers[value] = primaryUser;
                }

                UpdateFqdnManagerSelectedHeader();
                SyncManagementGroupComboFromSelection();

                _instructionFqdnSearchResultsListBox.SelectedItem = null;
                _instructionFqdnSearchResultsListBox.SelectedItems?.Clear();

                await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty);
                await UpdatePreviewAsync();
            }
            catch
            {
            }
        }


        private async void OnRemoveSelectedFqdnClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn || btn.Tag is not string fqdn)
                    return;

                var match = _fqdnManagerSelectedItems.FirstOrDefault(x =>
                    string.Equals(x, fqdn, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    _fqdnManagerSelectedItems.Remove(match);

                UpdateFqdnManagerSelectedHeader();
                SyncManagementGroupComboFromSelection();
                await UpdateInstructionFqdnSearchResultsAsync(_fqdnTextBox?.Text ?? string.Empty);
                await UpdatePreviewAsync();
            }
            catch
            {
            }
        }
        private async void OnClearSelectedFqdnsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                _fqdnManagerSelectedItems.Clear();
                UpdateFqdnManagerSelectedHeader();
                await UpdatePreviewAsync();
            }
            catch
            {
            }
        }

        private async void OnClearFqdnManagerClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                _fqdnManagerSelectedItems.Clear();

                UpdateFqdnManagerSelectedHeader();

                if (_fqdnTextBox != null)
                    _fqdnTextBox.Text = string.Empty;

                await UpdatePreviewAsync();
            }
            catch
            {
            }
        }
        private void OnCancelSelectedFqdnsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Keep the selected-targets pane visible in the bottom builder layout.
                if (_instructionFqdnSearchResultsListBox != null)
                {
                    _instructionFqdnSearchResultsListBox.SelectedItem = null;
                    _instructionFqdnSearchResultsListBox.SelectedItems?.Clear();
                }

                if (_fqdnTextBox != null)
                {
                    _fqdnTextBox.Focus();
                    _fqdnTextBox.CaretIndex = _fqdnTextBox.Text?.Length ?? 0;
                }
            }
            catch
            {
            }
        }

        private void OnClearSelectedFqdnManagerClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                _fqdnManagerSelectedItems.Clear();   // ✅ maintains binding
                UpdateFqdnManagerSelectedHeader();
                _ = UpdatePreviewAsync();
            }
            catch { }
        }


        private void OnSaveFqdnManagerClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_fqdnTextBox != null && _fqdnManagerSelectedItems.Count > 0)
                    _fqdnTextBox.Text = string.Empty;

                UpdateFqdnManagerSelectedHeader();
                _ = UpdatePreviewAsync();
            }
            catch { }
        }

        private void LoadSettingsFlyoutFromConfig()
        {
            try
            {
                if (_configHelper == null)
                    return;

                var cfg = _configHelper.GetConfiguration();

                var devReuseCb = this.FindControl<CheckBox>("EnableDevTokenReuseCheckBox");
                var apiLogCb = this.FindControl<CheckBox>("EnableApiTroubleshootingLogCheckBox");
                var showLogCb = this.FindControl<CheckBox>("ShowLogOnStartupCheckBox");
                var switchTabCb = this.FindControl<CheckBox>("SwitchToFqdnTabAfterSendCheckBox");
                var allowAllMgCb = this.FindControl<CheckBox>("AllowAllDevicesInManagementGroupDropdownCheckBox");

                var reauthTb = this.FindControl<TextBox>("ReauthMinutesTextBox");
                var resultLimitTb = this.FindControl<TextBox>("ResultDisplayLimitTextBox");
                var exportMaxTb = this.FindControl<TextBox>("ExportMaxRowsTextBox");
                var exportFmtCb = this.FindControl<ComboBox>("DefaultExportFormatComboBox");
                _exportFormatComboBox ??= exportFmtCb;

                bool hasDevReuse = cfg["EnableDevTokenReuse"] != null;
                bool hasApiLog = cfg["EnableApiTroubleshootingLog"] != null;
                bool hasAllowAll = cfg["AllowAllDevicesInManagementGroupDropdown"] != null;

                if (devReuseCb != null)
                {
                    devReuseCb.IsVisible = hasDevReuse;
                    if (hasDevReuse && bool.TryParse(cfg["EnableDevTokenReuse"], out var devReuse))
                        devReuseCb.IsChecked = devReuse;
                }

                if (apiLogCb != null)
                {
                    apiLogCb.IsVisible = hasApiLog;
                    if (hasApiLog && bool.TryParse(cfg["EnableApiTroubleshootingLog"], out var apiLog))
                        apiLogCb.IsChecked = apiLog;
                }

                if (showLogCb != null && bool.TryParse(cfg["ShowLogOnStartup"], out var showLog))
                    showLogCb.IsChecked = showLog;

                if (switchTabCb != null)
                {
                    // Default to true if missing.
                    if (bool.TryParse(cfg["SwitchToFqdnTabAfterSend"], out var switchTab))
                        switchTabCb.IsChecked = switchTab;
                    else
                        switchTabCb.IsChecked = true;
                }

                if (allowAllMgCb != null)
                {
                    allowAllMgCb.IsVisible = hasAllowAll;
                    if (hasAllowAll && bool.TryParse(cfg["AllowAllDevicesInManagementGroupDropdown"], out var allowAll))
                        allowAllMgCb.IsChecked = allowAll;
                    else
                        allowAllMgCb.IsChecked = false;
                }

                if (reauthTb != null)
                    reauthTb.Text = cfg["ReauthMinutes"] ?? "5";

                if (resultLimitTb != null)
                    resultLimitTb.Text = cfg["ResultDisplayLimit"] ?? "250";

                if (exportMaxTb != null)
                    exportMaxTb.Text = cfg["ExportMaxRows"] ?? "1000000";

                var fmt = (cfg["DefaultExportFormat"] ?? "xlsx").Trim().ToLowerInvariant();
                if (exportFmtCb != null)
                {
                    foreach (var item in exportFmtCb.Items.OfType<ComboBoxItem>())
                    {
                        var content = item.Content?.ToString()?.Trim()?.ToLowerInvariant();
                        if (content == fmt)
                        {
                            exportFmtCb.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Failed to load settings: {ex.Message}\n");
            }
        }

        private async void OnSaveSettingsClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Save platforms as part of overall Save action (single-save UX)
                try { await SavePlatformsInlineAsync(showStatusInLog: false); } catch { }

                // Read from UI
                var devReuseCb = this.FindControl<CheckBox>("EnableDevTokenReuseCheckBox");
                var apiLogCb = this.FindControl<CheckBox>("EnableApiTroubleshootingLogCheckBox");
                var showLogCb = this.FindControl<CheckBox>("ShowLogOnStartupCheckBox");
                var switchTabCb = this.FindControl<CheckBox>("SwitchToFqdnTabAfterSendCheckBox");

                var allowAllMgCb = this.FindControl<CheckBox>("AllowAllDevicesInManagementGroupDropdownCheckBox");

                var reauthTb = this.FindControl<TextBox>("ReauthMinutesTextBox");
                var resultLimitTb = this.FindControl<TextBox>("ResultDisplayLimitTextBox");
                var exportMaxTb = this.FindControl<TextBox>("ExportMaxRowsTextBox");
                var exportFmtCb = this.FindControl<ComboBox>("DefaultExportFormatComboBox");
                _exportFormatComboBox ??= exportFmtCb;

                bool enableDevReuse = devReuseCb?.IsChecked == true;
                bool enableApiLog = apiLogCb?.IsChecked == true;
                bool showLog = showLogCb?.IsChecked == true;
                bool switchToFqdnTabAfterSend = switchTabCb?.IsChecked != false; // default true

                bool allowAllDevicesInMgmtGroups = allowAllMgCb?.IsChecked == true;

                int reauthMinutes = 5;
                int.TryParse(reauthTb?.Text, out reauthMinutes);
                if (reauthMinutes <= 0) reauthMinutes = 5;

                int resultLimit = 250;
                int.TryParse(resultLimitTb?.Text, out resultLimit);
                if (resultLimit <= 0) resultLimit = 250;

                int exportMax = 1000000;
                int.TryParse(exportMaxTb?.Text, out exportMax);
                if (exportMax <= 0) exportMax = 1000000;

                string exportFmt = "xlsx";
                if (exportFmtCb?.SelectedItem is ComboBoxItem cbi && cbi.Content != null)
                    exportFmt = cbi.Content.ToString()?.Trim()?.ToLowerInvariant() ?? "xlsx";

                // Persist to appsettings.json (use the same resolved path for both reads and writes)
                var settingsPath = _configHelper?.ConfigFullPath ?? System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "appsettings.json");
                if (!System.IO.File.Exists(settingsPath))
                {
                    // Create a minimal config file so settings can be saved even on first run.
                    try { _configHelper?.SavePlatformConfigs(_configHelper?.GetPlatformConfigs() ?? new List<PlatformConfig>()); } catch { }
                }
                if (!System.IO.File.Exists(settingsPath))
                {
                    LogToUI($"❌ Settings file could not be created: {settingsPath}\n");
                    return;
                }

                var json = await System.IO.File.ReadAllTextAsync(settingsPath);
                var jObj = Newtonsoft.Json.Linq.JObject.Parse(json);

                if (devReuseCb?.IsVisible == true)
                    jObj["EnableDevTokenReuse"] = enableDevReuse;
                else
                    jObj.Remove("EnableDevTokenReuse");

                if (apiLogCb?.IsVisible == true)
                    jObj["EnableApiTroubleshootingLog"] = enableApiLog;
                else
                    jObj.Remove("EnableApiTroubleshootingLog");

                jObj["ShowLogOnStartup"] = showLog;
                jObj["SwitchToFqdnTabAfterSend"] = switchToFqdnTabAfterSend;

                if (allowAllMgCb?.IsVisible == true)
                    jObj["AllowAllDevicesInManagementGroupDropdown"] = allowAllDevicesInMgmtGroups;
                else
                    jObj.Remove("AllowAllDevicesInManagementGroupDropdown");
                jObj["ReauthMinutes"] = reauthMinutes;
                jObj["ResultDisplayLimit"] = resultLimit;
                jObj["ExportMaxRows"] = exportMax;
                jObj["DefaultExportFormat"] = exportFmt;

                await System.IO.File.WriteAllTextAsync(settingsPath, jObj.ToString(Newtonsoft.Json.Formatting.Indented));

                LogToUI("✅ Settings saved.\n");
                try { _config = _configHelper.GetConfiguration(); } catch { }

                // Do NOT call InitializePlatformAsync() on save.
                // It resets the dropdown selection to the first platform and re-triggers auth.
                // Instead, refresh only what must change immediately (auth settings already applied below).
                ReloadPlatformDropdownFromConfigPreserveSelection();


                // Apply some settings immediately (no restart needed)
                try
                {
                    _authService?.ApplySettings(enableDevReuse, reauthMinutes);
                }
                catch { }

                try
                {
                    // These reads happen via ConfigHelper, but update local cached values where applicable.
                    // Result display limit is read each time via ConfigHelper.GetMaxDisplayRows(), so no extra action required.
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to save settings: {ex.Message}\n");
            }
        }

        private void ReloadPlatformDropdownFromConfigPreserveSelection()
        {
            if (_platformUrlDropdown == null)
                return;

            string? previousUrl = null;
            if (_platformUrlDropdown.SelectedItem is DexInstructionRunner.Models.PlatformListItem prevItem)
                previousUrl = prevItem.Url;
            else if (_platformUrlDropdown.SelectedItem is string prevString)
                previousUrl = prevString;

            LoadConfig();

            _platformConfigs = _configHelper.GetPlatformConfigs();
            if (_platformConfigs == null || !_platformConfigs.Any())
                return;

            var items = _platformConfigs
                .Where(p => !string.IsNullOrWhiteSpace(p?.Url))
                .Select(p => new DexInstructionRunner.Models.PlatformListItem(
                    string.IsNullOrWhiteSpace(p?.Alias) ? DexInstructionRunner.Services.LogRedaction.SafeAliasFromHost(p!.Url) : p!.Alias,
                    p!.Url))
                .ToList();

            _suppressPlatformSelectionChanged = true;
            try
            {
                _platformUrlDropdown.ItemsSource = items;

                if (!string.IsNullOrWhiteSpace(previousUrl))
                {
                    var match = items.FirstOrDefault(i => string.Equals(i.Url, previousUrl, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _platformUrlDropdown.SelectedItem = match;
                        return;
                    }
                }

                // Only fall back if the previous platform no longer exists
                _platformUrlDropdown.SelectedItem = items.FirstOrDefault();
            }
            finally
            {
                _suppressPlatformSelectionChanged = false;
            }
        }

        private void LoadPlatformsIntoSettingsFlyout()
        {
            try
            {
                var grid = this.FindControl<Avalonia.Controls.DataGrid>("PlatformsDataGrid");
                if (grid == null || _configHelper == null)
                    return;

                _settingsPlatformRows.Clear();
                _currentDefaultPlatformAlias = _configHelper.GetDefaultPlatformAlias();
                _pendingDefaultPlatformAlias = _currentDefaultPlatformAlias;

                foreach (var platform in _configHelper.GetPlatformConfigs())
                {
                    var alias = string.IsNullOrWhiteSpace(platform.Alias) ? ConfigHelper.DeriveAliasFromHost(platform.Url) : platform.Alias.Trim();
                    var row = new PlatformRowView
                    {
                        Alias = alias,
                        UrlPlain = ConfigHelper.NormalizePlatformUrl(platform.Url),
                        OriginalUrlRaw = string.Empty,
                        OriginalWasEncrypted = true,
                        IsNewRow = false,
                        UrlEdit = string.Empty
                    };
                    HookPlatformRowDirtyTracking(row);
                    _settingsPlatformRows.Add(row);
                }

                var sorted = _settingsPlatformRows
                    .OrderBy(p => string.IsNullOrWhiteSpace(p.Alias) ? p.UrlPlain : p.Alias, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.UrlPlain, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _settingsPlatformRows.Clear();
                foreach (var row in sorted)
                {
                    HookPlatformRowDirtyTracking(row);
                    _settingsPlatformRows.Add(row);
                }

                var newRow = new PlatformRowView
                {
                    Alias = string.Empty,
                    UrlPlain = string.Empty,
                    OriginalUrlRaw = string.Empty,
                    OriginalWasEncrypted = true,
                    IsNewRow = true,
                    UrlEdit = string.Empty
                };
                HookPlatformRowDirtyTracking(newRow);
                _settingsPlatformRows.Add(newRow);

                grid.ItemsSource = _settingsPlatformRows;
                _platformSettingsDirty = false;
                UpdatePlatformsInlineStatusText();
                UpdatePlatformDefaultMarkers();
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Failed to load platforms: {ex.Message}"); } catch { }
            }
        }

        private void HookPlatformRowDirtyTracking(PlatformRowView row)
        {
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PlatformRowView.Alias) ||
                    args.PropertyName == nameof(PlatformRowView.UrlEdit))
                {
                    _platformSettingsDirty = true;
                    UpdatePlatformsInlineStatusText();
                }
            };
        }

        private static string NormalizePlatformUrlForUi(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            url = url.Trim();

            // remove surrounding quotes
            if ((url.StartsWith("\"") && url.EndsWith("\"")) || (url.StartsWith("'") && url.EndsWith("'")))
                url = url.Substring(1, url.Length - 2).Trim();

            // If a full URL was pasted, prefer Uri parsing for safety.
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    url = uri.Host;
                }
                else
                {
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        url = url.Substring(7);
                    else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = url.Substring(8);
                }
            }

            // strip path
            var slash = url.IndexOf("/");
            if (slash >= 0)
                url = url.Substring(0, slash);

            // strip port
            var colon = url.IndexOf(":");
            if (colon >= 0)
                url = url.Substring(0, colon);

            url = url.Trim().TrimEnd('.').TrimEnd('/');

            return url;
        }

        private void UpdatePlatformsInlineStatusText()
        {
            try
            {
                var tb = this.FindControl<TextBlock>("PlatformsInlineStatusText");
                if (tb == null)
                    return;

                var def = string.IsNullOrWhiteSpace(_pendingDefaultPlatformAlias) ? string.Empty : $"Default: {_pendingDefaultPlatformAlias}";

                if (_platformSettingsDirty)
                    tb.Text = string.IsNullOrWhiteSpace(def) ? "Unsaved changes" : $"Unsaved changes • {def}";
                else
                    tb.Text = def;

                // Keep the Default column indicator in sync with the pending selection.
                UpdatePlatformDefaultMarkers();
            }
            catch
            {
                // ignore
            }
        }

        private void UpdatePlatformDefaultMarkers()
        {
            try
            {
                var pending = (_pendingDefaultPlatformAlias ?? string.Empty).Trim();

                foreach (var row in _settingsPlatformRows)
                {
                    if (row == null)
                        continue;

                    if (row.IsNewRow)
                    {
                        row.DefaultMarker = string.Empty;
                        continue;
                    }

                    var alias = (row.Alias ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(alias))
                        alias = (row.UrlPlain ?? string.Empty).Trim();

                    if (!string.IsNullOrWhiteSpace(pending) &&
                        string.Equals(alias, pending, StringComparison.OrdinalIgnoreCase))
                    {
                        row.DefaultMarker = "★";
                    }
                    else
                    {
                        row.DefaultMarker = string.Empty;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private async void OnSavePlatformsInlineClicked(object? sender, RoutedEventArgs e)
        {
            await SavePlatformsInlineAsync(showStatusInLog: true);
        }

        private void OnSetDefaultPlatformInlineClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                var grid = this.FindControl<Avalonia.Controls.DataGrid>("PlatformsDataGrid");
                if (grid?.SelectedItem is not PlatformRowView row)
                    return;

                if (row.IsNewRow)
                    return;

                var alias = (row.Alias ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(alias))
                    alias = (row.UrlPlain ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(alias))
                    return;

                _pendingDefaultPlatformAlias = alias;
                _platformSettingsDirty = true;
                UpdatePlatformsInlineStatusText();

                try { LogToUI($"✅ Default platform set to: {alias}"); } catch { }
            }
            catch
            {
                // ignore
            }
        }

        private void OnRemovePlatformsInlineClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                var grid = this.FindControl<Avalonia.Controls.DataGrid>("PlatformsDataGrid");
                if (grid?.SelectedItem is not PlatformRowView row)
                    return;

                if (row.IsNewRow)
                    return;

                _settingsPlatformRows.Remove(row);
                _platformSettingsDirty = true;
                UpdatePlatformsInlineStatusText();
            }
            catch
            {
                // ignore
            }
        }

        private async Task<bool> SavePlatformsInlineAsync(bool showStatusInLog)
        {
            try
            {
                if (_configHelper == null)
                    return false;

                var output = new List<PlatformConfig>();
                foreach (var row in _settingsPlatformRows)
                {
                    if (row == null)
                        continue;

                    var alias = ConfigHelper.SanitizeAlias((row.Alias ?? string.Empty).Trim());
                    var urlPlain = ConfigHelper.NormalizePlatformUrl((row.UrlPlain ?? string.Empty).Trim());
                    var urlEdit = ConfigHelper.NormalizePlatformUrl((row.UrlEdit ?? string.Empty).Trim());

                    if (row.IsNewRow)
                    {
                        if (string.IsNullOrWhiteSpace(urlEdit) && string.IsNullOrWhiteSpace(alias))
                            continue;

                        if (string.IsNullOrWhiteSpace(urlEdit))
                        {
                            await ShowSimpleDialogAsync("Platforms", "Platform URL is required for a new entry.");
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(alias))
                            alias = ConfigHelper.DeriveAliasFromHost(urlEdit);

                        if (!ConfigHelper.IsValidAlias(alias))
                        {
                            await ShowSimpleDialogAsync("Platforms", "Alias is required and may contain only letters, numbers, dot, underscore, and dash.");
                            return false;
                        }

                        output.Add(new PlatformConfig { Alias = alias, Url = urlEdit, DefaultMG = string.Empty, Consumer = "Explorer" });
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(urlEdit))
                        urlPlain = urlEdit;

                    if (string.IsNullOrWhiteSpace(urlPlain))
                        continue;

                    if (string.IsNullOrWhiteSpace(alias))
                        alias = ConfigHelper.DeriveAliasFromHost(urlPlain);

                    if (!ConfigHelper.IsValidAlias(alias))
                    {
                        await ShowSimpleDialogAsync("Platforms", "Alias is required and may contain only letters, numbers, dot, underscore, and dash.");
                        return false;
                    }

                    output.Add(new PlatformConfig { Alias = alias, Url = urlPlain, DefaultMG = string.Empty, Consumer = "Explorer" });
                }

                if (output.GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                {
                    await ShowSimpleDialogAsync("Platforms", "Aliases must be unique.");
                    return false;
                }

                if (!_configHelper.SavePlatformConfigs(output))
                {
                    await ShowSimpleDialogAsync("Platforms", "Failed to save encrypted platform entries.");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(_pendingDefaultPlatformAlias))
                    _configHelper.SetDefaultPlatformAlias(_pendingDefaultPlatformAlias);

                LoadPlatformsIntoSettingsFlyout();
                ReloadPlatformDropdownFromConfigPreserveSelection();
                _platformSettingsDirty = false;
                UpdatePlatformsInlineStatusText();

                // If this was the first save, or the selected/default platform changed,
                // immediately activate the platform without requiring an app restart.
                try
                {
                    LoadConfig();
                    _platformConfigs = _configHelper.GetPlatformConfigs();

                    if (_platformConfigs != null && _platformConfigs.Any())
                    {
                        var preferredAlias = _configHelper.GetDefaultPlatformAlias();

                        var selectedPlatform =
                            !string.IsNullOrWhiteSpace(preferredAlias)
                                ? _platformConfigs.FirstOrDefault(p => string.Equals(p?.Alias, preferredAlias, StringComparison.OrdinalIgnoreCase))
                                : null;

                        selectedPlatform ??= _platformConfigs.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p?.Url));

                        if (selectedPlatform != null && !string.IsNullOrWhiteSpace(selectedPlatform.Url))
                        {
                            var items = _platformConfigs
                                .Where(p => !string.IsNullOrWhiteSpace(p?.Url))
                                .Select(p => new DexInstructionRunner.Models.PlatformListItem(
                                    string.IsNullOrWhiteSpace(p?.Alias)
                                        ? DexInstructionRunner.Services.LogRedaction.SafeAliasFromHost(p!.Url)
                                        : p!.Alias,
                                    p!.Url))
                                .ToList();

                            _suppressPlatformSelectionChanged = true;
                            try
                            {
                                _platformUrlDropdown.ItemsSource = items;
                                _platformUrlDropdown.SelectedItem =
                                    items.FirstOrDefault(i => string.Equals(i.Url, selectedPlatform.Url, StringComparison.OrdinalIgnoreCase))
                                    ?? items.FirstOrDefault();
                            }
                            finally
                            {
                                _suppressPlatformSelectionChanged = false;
                            }

                            await HandlePlatformSelectedAsync(selectedPlatform.Url, isInitialLoad: false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"⚠️ Platform saved, but immediate platform activation failed: {ex.Message}\n");
                }

                if (showStatusInLog)
                    LogToUI($"✅ Saved {output.Count} platform entr{(output.Count == 1 ? "y" : "ies")}.");

                return true;
            }
            catch (Exception ex)
            {
                if (showStatusInLog)
                    LogToUI($"❌ Failed to save platforms: {ex.Message}");
                return false;
            }
        }

        private async Task ShowSimpleDialogAsync(string title, string message)
        {
            try
            {
                var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Width = 90 };

                var wnd = new Window
                {
                    Title = title,
                    Width = 420,
                    Height = 160,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(14),
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = message ?? string.Empty, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                            ok
                        }
                    }
                };

                ok.Click += (_, __) =>
                {
                    try { wnd.Close(); } catch { }
                };

                await wnd.ShowDialog(this);
            }
            catch
            {
                try { LogToUI($"⚠️ {title}: {message}\n"); } catch { }
            }
        }

        private async Task<bool> ShowYesNoDialogAsync(string title, string message, string yesText = "Yes", string noText = "No")
        {
            try
            {
                bool result = false;

                var yes = new Button { Content = yesText, HorizontalAlignment = HorizontalAlignment.Right, Width = 90 };
                var no = new Button { Content = noText, HorizontalAlignment = HorizontalAlignment.Right, Width = 90 };

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { no, yes }
                };

                var wnd = new Window
                {
                    Title = title,
                    Width = 520,
                    Height = 190,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(16),
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock { Text = message ?? string.Empty, TextWrapping = TextWrapping.Wrap },
                            buttons
                        }
                    }
                };

                yes.Click += (_, __) =>
                {
                    result = true;
                    try { wnd.Close(); } catch { }
                };

                no.Click += (_, __) =>
                {
                    result = false;
                    try { wnd.Close(); } catch { }
                };

                await wnd.ShowDialog(this);
                return result;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateInstructionRunScopeUi()
        {
            // Tab content may not be materialized until selected; lazily re-find controls.
            if (_runScopeAllRadio == null)
                _runScopeAllRadio = TryFindControl<RadioButton>("RunScopeAllRadio");
            if (_runScopeFilteredRadio == null)
                _runScopeFilteredRadio = TryFindControl<RadioButton>("RunScopeFilteredRadio");
            if (_instrRunFilterButton == null)
                _instrRunFilterButton = TryFindControl<Button>("InstrRunFilterButton");

            var isFiltered = _runScopeFilteredRadio?.IsChecked == true;

            if (_instrRunFilterButton != null)
                _instrRunFilterButton.IsEnabled = isFiltered;

            if (!isFiltered)
            {
                // Switching back to All must close and clear the run filter.
                CloseAttachedFlyout(_instrRunFilterButton);
                ClearInstructionRunFilter();
            }

            UpdateInstructionResultFilterSummary();
        }

        private void RunScopeAllRadio_Checked(object? sender, RoutedEventArgs e)
        {
            try
            {
                UpdateInstructionRunScopeUi();
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Run scope update failed: {ex.Message}\n"); } catch { }
            }
        }

        private async void RunScopeFilteredRadio_Checked(object? sender, RoutedEventArgs e)
        {
            try
            {
                await OnInstructionRunScopeFilteredSelectedAsync();
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Filtered scope selection failed: {ex.Message}\n"); } catch { }
            }
        }

        private async void RerunInstructionButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var newRunId = await RerunInstructionAsync();
                if (newRunId == null || newRunId <= 0)
                {
                    LogToUI("❌ Rerun did not return a new run/execution id.");
                    return;
                }

                // Switch to Results and bind to the new run
                var tabControl = this.FindControl<TabControl>("MainTabControl");
                if (tabControl != null)
                    tabControl.SelectedIndex = 2;

                await ActivateResultsForRunAsync(newRunId.Value);
            }
            catch (Exception ex)
            {
                try { LogToUI($"❌ Rerun click failed: {ex.Message}\n"); } catch { }
            }
        }


        private void UpdateFqdnRunScopeUi()
        {
            var isFiltered = _fqdnRunScopeFilteredRadio?.IsChecked == true;

            if (_fqdnRunFilterButton != null)
                _fqdnRunFilterButton.IsEnabled = isFiltered;

            if (!isFiltered)
            {
                // Switching back to All must close and clear the run filter.
                CloseAttachedFlyout(_fqdnRunFilterButton);
                ClearFqdnRunFilter();
            }

            UpdateFqdnResultFilterSummary();
        }

        private async Task ActivateResultsForRunAsync(int runId)
        {
            // Switch Results live polling to the new run id
            _currentInstructionId = runId.ToString();
            _activeResultsInstructionId = runId;
            _lastInstructionId = runId;

            _activeResultsExpiresUtc = null;

            // Reset progress tracking so UI updates for the new run
            _lastProgressSentCount = -1;
            _lastProgressReceivedCount = -1;
            _lastProgressOutstandingCount = -1;

            _lastPeriodicResultsRefreshUtc = DateTime.MinValue;
            _lastAutoResultsRefreshUtc = DateTime.MinValue;

            // Set immediate non-expired status (poll will refine it)
            var statusTextBlock = this.FindControl<TextBlock>("ResultsStatusText");
            if (statusTextBlock != null)
            {
                statusTextBlock.Text = "Status: In Progress";
                statusTextBlock.Foreground = Brushes.Orange;
            }

            // Clear view + load results for the new run
            this.FindControl<StackPanel>("ResultsPanel")?.Children.Clear();
            this.FindControl<RadioButton>("RawViewRadioButton")!.IsChecked = true;

            await LoadInstructionResultsPageAsync(runId, startRange: null, resetPanels: true);

            // Invalidate any in-flight status pollers and restart for this run
            _resultsStatusPollRunId++;
            var pollRunId = _resultsStatusPollRunId;

            _ = RefreshInstructionStatusAsync(runId.ToString(), pollRunId);

            // Kick progress/coverage refresh for the new run
            _ = ShowInstructionProgressAsync(runId, log: false);
        }

        private async Task OnInstructionRunScopeFilteredSelectedAsync()
        {
            UpdateInstructionRunScopeUi();
            await EnsureSelectedInstructionSchemaLoadedAsync();
            ApplySchemaColumnsToRunFilterUi();

            // When opening the flyout, reflect the last applied filters (if any)
            PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: false);

            // Show the flyout when the user selects Filtered.
            Dispatcher.UIThread.Post(() => ShowAttachedFlyout(_instrRunFilterButton));
        }

        private async Task OnFqdnRunScopeFilteredSelectedAsync()
        {
            UpdateFqdnRunScopeUi();
            await EnsureSelectedInstructionSchemaLoadedAsync(forFqdnTab: true);
            ApplySchemaColumnsToRunFilterUi();

            // When opening the flyout, reflect the last applied filters (if any)
            PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: true);

            // Show the flyout when the user selects Filtered.
            // The FQDN tab button is often inside a scroll viewer; defer the show until the next UI tick.
            Dispatcher.UIThread.Post(() => ShowAttachedFlyout(_fqdnRunFilterButton));
        }


        private void InstrRunFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_runScopeFilteredRadio?.IsChecked == true)
            {
                PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: false);
                Dispatcher.UIThread.Post(() => ShowAttachedFlyout(_instrRunFilterButton));
            }
        }


        private void InstrRunFilterCancel_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CancelInstrRunFilterEdit();
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Cancel filter failed: {ex.Message}\n"); } catch { }
            }
        }

        private void InstrRunFilterApply_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ApplyInstrRunFilterFromFlyout();
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Apply filter failed: {ex.Message}\n"); } catch { }
            }
        }

        private void InstrRunFilterClear_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Clear the active instruction run filter and update summary/clear button visibility.
                ClearInstructionRunFilter();

                // If the flyout is open, close it.
                CloseAttachedFlyout(_instrRunFilterButton);
            }
            catch (Exception ex)
            {
                try { LogToUI($"⚠️ Clear filter failed: {ex.Message}\n"); } catch { }
            }
        }

        private void FqdnRunFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_fqdnRunScopeFilteredRadio?.IsChecked == true)
            {
                PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: true);
                Dispatcher.UIThread.Post(() => ShowAttachedFlyout(_fqdnRunFilterButton));
            }
        }

        private static void ShowAttachedFlyout(Control? control)
        {
            if (control == null) return;
            try { FlyoutBase.ShowAttachedFlyout(control); } catch { }
        }

        private static void CloseAttachedFlyout(Control? control)
        {
            if (control == null) return;
            try { FlyoutBase.GetAttachedFlyout(control)?.Hide(); } catch { }
        }

        private async Task EnsureSelectedInstructionSchemaLoadedAsync(bool forFqdnTab = false)
        {
            if (_selectedPlatform == null || string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(_consumerName))
                return;

            int? defId = null;

            if (!forFqdnTab)
            {
                if (_instructionListBox?.SelectedItem is string name && _instructionMap.TryGetValue(name, out int id))
                    defId = id;
            }
            else
            {
                var lb = this.FindControl<ListBox>("FqdnInstructionListBox");
                if (lb?.SelectedItem is string name && _instructionMap.TryGetValue(name, out int id))
                    defId = id;
            }

            if (defId == null || defId <= 0)
                return;

            // If we already have schema columns for this definition, keep them
            if (_currentInstructionSchemaDefinitionId == defId.Value && _currentInstructionSchemaColumns.Count > 0)
                return;

            _currentInstructionSchemaDefinitionId = defId.Value;
            _currentInstructionSchemaColumns.Clear();
            _runFilterColumnTypeMap.Clear();
            RunFilterAvailableColumns.Clear();

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var url = $"https://{_selectedPlatform.Url}/consumer/InstructionDefinitions/id/{defId.Value}";
                var json = await ApiLogger.LogApiCallAsync(
                    label: "InstructionDefinitionById",
                    endpoint: $"consumer/InstructionDefinitions/id/{defId.Value}",
                    apiCall: async () =>
                    {
                        var resp = await client.GetAsync(url);
                        return await resp.Content.ReadAsStringAsync();
                    },
                    payloadJson: ""
                );

                var obj = JObject.Parse(json);
                var schema = obj["Schema"] as JArray;

                if (schema != null)
                {
                    foreach (var s in schema)
                    {
                        var col = s?["Name"]?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(col))
                            continue;

                        var type = s?["Type"]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(type))
                            _runFilterColumnTypeMap[col] = type;

                        _currentInstructionSchemaColumns.Add(col);
                    }
                }

                _currentInstructionSchemaColumns = _currentInstructionSchemaColumns
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                LogToUI($"✅ Loaded instruction schema columns: {_currentInstructionSchemaColumns.Count}\n");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Failed to load instruction definition/schema: {ex.Message}\n");
            }
        }
        private void ApplySchemaColumnsToRunFilterUi()
        {
            // Schema drives the run filter UI 100%.
            RunFilterAvailableColumns.Clear();
            foreach (var c in _currentInstructionSchemaColumns)
                RunFilterAvailableColumns.Add(c);

            // Build one row per schema column (matches Results header filter behavior).
            RebuildRunFilterRowsFromSchema(isFqdnTab: false);
            RebuildRunFilterRowsFromSchema(isFqdnTab: true);

            UpdateInstructionResultFilterSummary();
            UpdateFqdnResultFilterSummary();
        }

        private void RebuildRunFilterRowsFromSchema(bool isFqdnTab)
        {
            var rows = isFqdnTab ? FqdnRunFilterRows : InstrRunFilterRows;
            var active = isFqdnTab ? _activeFqdnRunFilters : _activeInstrRunFilters;

            rows.Clear();

            var rowMap = new Dictionary<string, RunResultFilterRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in _currentInstructionSchemaColumns)
            {
                if (string.IsNullOrWhiteSpace(col))
                    continue;

                var row = new RunResultFilterRow
                {
                    Column = col.Trim(),
                    Operator = string.Empty,
                    Value = string.Empty,
                    DataType = NormalizeSchemaType(ResolveSchemaType(col))
                };

                row.PropertyChanged += RunFilterRow_PropertyChanged;
                EnsureRunFilterRowOperators(row);

                rows.Add(row);
                if (!rowMap.ContainsKey(row.Column))
                    rowMap[row.Column] = row;
            }

            // Apply any currently active filters into the schema rows.
            foreach (var c in active)
            {
                var col = (c.Column ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(col))
                    continue;

                if (!rowMap.TryGetValue(col, out var row))
                    continue;

                if (!string.IsNullOrWhiteSpace(c.DataType))
                    row.DataType = string.IsNullOrWhiteSpace(c.DataType) ? row.DataType : c.DataType;

                row.Operator = (c.Operator ?? string.Empty).Trim();
                row.Value = (c.Value ?? string.Empty).Trim();

                EnsureRunFilterRowOperators(row);
            }
        }

        private static void ClearRunFilterRowValues(System.Collections.ObjectModel.ObservableCollection<RunResultFilterRow> rows)
        {
            foreach (var r in rows)
            {
                r.Operator = string.Empty;
                r.Value = string.Empty;
            }
        }

        private void RefreshRunFilterRowOperators(ObservableCollection<RunResultFilterRow> rows)
        {
            foreach (var row in rows)
                EnsureRunFilterRowOperators(row);
        }

        private void EnsureRunFilterRowOperators(RunResultFilterRow row)
        {
            var col = (row.Column ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(col))
            {
                row.AvailableOperators.Clear();
                row.Operator = string.Empty;
                row.DataType = "string";
                return;
            }

            var rawType = ResolveSchemaType(col);
            var normalized = NormalizeSchemaType(rawType);
            row.DataType = normalized;

            var ops = normalized == "number"
                ? new[] { "=", "!=", ">", ">=", "<", "<=" }
                : new[] { "contains", "equals", "not equals", "starts with", "ends with" };

            row.AvailableOperators.Clear();
            foreach (var o in ops)
                row.AvailableOperators.Add(o);

            if (string.IsNullOrWhiteSpace(row.Operator) || !row.AvailableOperators.Contains(row.Operator))
                row.Operator = row.AvailableOperators.FirstOrDefault() ?? string.Empty;
        }

        private string ResolveSchemaType(string column)
        {
            if (string.IsNullOrWhiteSpace(column))
                return "string";

            if (_runFilterColumnTypeMap.TryGetValue(column.Trim(), out var t) && !string.IsNullOrWhiteSpace(t))
                return t;

            return "string";
        }

        private static string NormalizeSchemaType(string rawType)
        {
            var t = (rawType ?? string.Empty).Trim().ToLowerInvariant();
            if (t.Contains("int") || t.Contains("long") || t.Contains("short") || t.Contains("float") || t.Contains("double") || t.Contains("decimal") || t.Contains("number"))
                return "number";
            return "string";
        }

        private void ClearInstructionRunFilter()
        {
            _activeInstrRunFilters.Clear();
            ClearRunFilterRowValues(InstrRunFilterRows);
            UpdateInstructionResultFilterSummary();
        }


        private void ClearFqdnRunFilter()
        {
            _activeFqdnRunFilters.Clear();
            ClearRunFilterRowValues(FqdnRunFilterRows);
            UpdateFqdnResultFilterSummary();
        }


        private void ClearInstrRunFilterFlyoutInputs()
        {
            ClearRunFilterRowValues(InstrRunFilterRows);
        }


        private void ClearFqdnRunFilterFlyoutInputs()
        {
            ClearRunFilterRowValues(FqdnRunFilterRows);
        }


        private void PopulateRunFilterFlyoutInputsFromActive(bool isFqdnTab)
        {
            // Keep schema rows and only apply the active filter values/operators.
            RebuildRunFilterRowsFromSchema(isFqdnTab);
        }

        private void RunFilterRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not RunResultFilterRow row)
                return;

            if (e.PropertyName == nameof(RunResultFilterRow.Column))
            {
                EnsureRunFilterRowOperators(row);
            }
        }

        private void InstrRunFilterAddRow_Click(object? sender, RoutedEventArgs e)
        {
            var row = new RunResultFilterRow();
            row.PropertyChanged += RunFilterRow_PropertyChanged;
            InstrRunFilterRows.Add(row);
        }

        private void FqdnRunFilterAddRow_Click(object? sender, RoutedEventArgs e)
        {
            var row = new RunResultFilterRow();
            row.PropertyChanged += RunFilterRow_PropertyChanged;
            FqdnRunFilterRows.Add(row);
        }

        private void InstrRunFilterRemoveRow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is RunResultFilterRow row)
                InstrRunFilterRows.Remove(row);
        }

        private void FqdnRunFilterRemoveRow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is RunResultFilterRow row)
                FqdnRunFilterRows.Remove(row);
        }

        private void ApplyInstrRunFilterFromFlyout()
        {
            _activeInstrRunFilters.Clear();
            _activeInstrRunFilters.AddRange(GetRunFilterClausesFromRows(isFqdnTab: false));
            UpdateInstructionResultFilterSummary();
            CloseAttachedFlyout(_instrRunFilterButton);
        }

        private void ApplyFqdnRunFilterFromFlyout()
        {
            _activeFqdnRunFilters.Clear();
            _activeFqdnRunFilters.AddRange(GetRunFilterClausesFromRows(isFqdnTab: true));
            UpdateFqdnResultFilterSummary();
            CloseAttachedFlyout(_fqdnRunFilterButton);
        }

        private void CancelInstrRunFilterEdit()
        {
            PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: false);
            CloseAttachedFlyout(_instrRunFilterButton);
        }

        private void CancelFqdnRunFilterEdit()
        {
            PopulateRunFilterFlyoutInputsFromActive(isFqdnTab: true);
            CloseAttachedFlyout(_fqdnRunFilterButton);
        }

        private void UpdateInstructionResultFilterSummary()
        {
            // Tab content may not be materialized until selected; lazily re-find controls.
            if (_instrFilterSummaryText == null)
                _instrFilterSummaryText = this.FindControl<TextBlock>("InstrFilterSummaryText");
            if (_instrFilterClearButton == null)
                _instrFilterClearButton = this.FindControl<Button>("InstrFilterClearButton");
            if (_instrFilterSummaryText == null)
                return;

            var parts = GetRunFilterParts(isFqdnTab: false);
            if (parts.Count == 0)
            {
                _instrFilterSummaryText.Text = "No run filter";
                if (_instrFilterClearButton != null) _instrFilterClearButton.IsVisible = false;
                return;
            }

            _instrFilterSummaryText.Text = string.Join(" AND ", parts);
            if (_instrFilterClearButton != null) _instrFilterClearButton.IsVisible = true;
        }

        private void UpdateFqdnResultFilterSummary()
        {
            // Tab content may not be materialized until selected; lazily re-find controls.
            if (_fqdnFilterSummaryText == null)
                _fqdnFilterSummaryText = this.FindControl<TextBlock>("FqdnFilterSummaryText");
            if (_fqdnFilterClearButton == null)
                _fqdnFilterClearButton = this.FindControl<Button>("FqdnFilterClearButton");
            if (_fqdnFilterSummaryText == null)
                return;

            var parts = GetRunFilterParts(isFqdnTab: true);
            if (parts.Count == 0)
            {
                _fqdnFilterSummaryText.Text = "No run filter";
                if (_fqdnFilterClearButton != null) _fqdnFilterClearButton.IsVisible = false;
                return;
            }

            _fqdnFilterSummaryText.Text = string.Join(" AND ", parts);
            if (_fqdnFilterClearButton != null) _fqdnFilterClearButton.IsVisible = true;
        }

        private List<RunResultFilterClause> GetRunFilterClausesFromRows(bool isFqdnTab)
        {
            var rows = isFqdnTab ? FqdnRunFilterRows : InstrRunFilterRows;
            var clauses = new List<RunResultFilterClause>();

            foreach (var row in rows)
            {
                var col = (row.Column ?? string.Empty).Trim();
                var val = (row.Value ?? string.Empty).Trim();
                var op = (row.Operator ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(col) || string.IsNullOrWhiteSpace(val))
                    continue;

                var rawType = ResolveSchemaType(col);
                var normalized = NormalizeSchemaType(rawType);

                if (string.IsNullOrWhiteSpace(op))
                    op = normalized == "number" ? "=" : "contains";

                clauses.Add(new RunResultFilterClause
                {
                    Column = col,
                    Operator = op,
                    Value = val,
                    DataType = normalized
                });
            }

            // De-dup by (col, op, val) to prevent accidental double rows.
            return clauses
                .GroupBy(c => (c.Column.ToLowerInvariant(), c.Operator.ToLowerInvariant(), c.Value.ToLowerInvariant()))
                .Select(g => g.First())
                .ToList();
        }

        private List<string> GetRunFilterParts(bool isFqdnTab)
        {
            var clauses = isFqdnTab ? _activeFqdnRunFilters : _activeInstrRunFilters;
            var parts = new List<string>();

            foreach (var c in clauses)
            {
                var col = c.Column;
                var op = c.Operator;
                var val = c.Value;

                if (string.IsNullOrWhiteSpace(col) || string.IsNullOrWhiteSpace(op) || string.IsNullOrWhiteSpace(val))
                    continue;

                if (c.DataType == "number")
                {
                    parts.Add($"{col} {op} {val}");
                }
                else
                {
                    parts.Add(op switch
                    {
                        "contains" => $"{col} contains \"{val}\"",
                        "equals" => $"{col} equals \"{val}\"",
                        "not equals" => $"{col} not equals \"{val}\"",
                        "starts with" => $"{col} starts with \"{val}\"",
                        "ends with" => $"{col} ends with \"{val}\"",
                        _ => $"{col} {op} \"{val}\""
                    });
                }
            }

            return parts;
        }

        private object? BuildRunResultsFilterObject(bool isFqdnTab)
        {
            var clauses = (isFqdnTab ? _activeFqdnRunFilters : _activeInstrRunFilters).ToList();
            if (clauses.Count == 0)
                return null;

            object makeOperand(RunResultFilterClause c)
            {
                var normalizedType = string.IsNullOrWhiteSpace(c.DataType) ? "string" : c.DataType;
                var op = (c.Operator ?? string.Empty).Trim();
                var val = (c.Value ?? string.Empty).Trim();

                string opName = "==";
                string finalValue = val;

                if (normalizedType == "number")
                {
                    // Keep numeric comparisons as symbols; use == for equality for consistency with API.
                    opName = op switch
                    {
                        "=" => "==",
                        "==" => "==",
                        "!=" => "!=",
                        ">" => ">",
                        ">=" => ">=",
                        "<" => "<",
                        "<=" => "<=",
                        _ => "=="
                    };

                    return new
                    {
                        Attribute = c.Column,
                        Operator = opName,
                        Value = val,
                        DataType = "number"
                    };
                }

                // string (API accepts Like + comparison operators)
                switch (op)
                {
                    case "contains":
                        opName = "Like";
                        finalValue = $"%{val}%";
                        break;

                    case "starts with":
                        opName = "Like";
                        finalValue = $"{val}%";
                        break;

                    case "ends with":
                        opName = "Like";
                        finalValue = $"%{val}";
                        break;

                    case "equals":
                        opName = "==";
                        finalValue = val;
                        break;

                    case "not equals":
                        opName = "!=";
                        finalValue = val;
                        break;

                    default:
                        opName = "Like";
                        finalValue = $"%{val}%";
                        break;
                }

                return new
                {
                    Attribute = c.Column,
                    Operator = opName,
                    Value = finalValue,
                    DataType = "string"
                };
            }

            // IMPORTANT:
            // The web UI sends a single operand object for a single filter, and uses title-case operators
            // (Like/And). Sending an AND-group with a single operand can cause the server to emit invalid
            // SQL like: "where AND (...)".
            if (clauses.Count == 1)
                return makeOperand(clauses[0]);

            return new
            {
                Operator = "And",
                Operands = clauses.Select(makeOperand).ToList()
            };
        }
        public readonly struct RunResultFilterClause
        {
            public string Column { get; init; }
            public string Operator { get; init; }
            public string Value { get; init; }
            public string DataType { get; init; }
        }
    }
}