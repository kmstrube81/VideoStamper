using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia; 
using Avalonia.Controls;
using Avalonia.Controls.Primitives; 
using Avalonia.Interactivity;
using Avalonia.Media; 
using Avalonia.Platform.Storage;
using VideoStamper.Gui.Models;
using System.Threading;
using Avalonia.Layout;

namespace VideoStamper.Gui;

public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _jsonOptions =
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };


    private readonly ObservableCollection<InputSettings> _inputs = new();
    private int _currentInputIndex = -1;

    // Tracks whether the current in-memory state differs from the last export.
    // (This is intentionally conservative; we mark dirty on most user-driven changes.)
    private bool _isDirty;
    private bool _suppressClosePrompt;
    private bool _closePromptOpen;
    private bool _isProcessing;

    private readonly string _settingsPath;
    private AppSettings _settings = new();

    private readonly ObservableCollection<FontOption> _fontOptions = new();
    private FontOption? _addCustomFontOption;

    private sealed class FontOption
    {
        public string Name { get; }
        public string Path { get; }
        public bool IsAddCustom { get; }

        public FontOption(string name, string path, bool isAddCustom = false)
        {
            Name = name;
            Path = path;
            IsAddCustom = isAddCustom;
        }

        public override string ToString() => Name;
    }

    //public IEnumerable<string> FontOptions => _fontOptions.Select(f => f.Path);
    public IEnumerable FontOptions => _fontOptions;

    private sealed class BorderColorOption
    {
        public string Label { get; }
        public string? Value { get; }  // null means "No Border"

        public BorderColorOption(string label, string? value)
        {
            Label = label;
            Value = value;
        }

        public override string ToString() => Label;
    }

    private static readonly BorderColorOption[] BorderColorOptionsInternal =
    {
        new BorderColorOption("No Border", null),
        new BorderColorOption("Black", "black"),
        new BorderColorOption("White", "white"),
        new BorderColorOption("Red", "red"),
        new BorderColorOption("Yellow", "yellow"),
        new BorderColorOption("Blue", "blue"),
        new BorderColorOption("Gray", "gray"),
        new BorderColorOption("Brown", "brown"),
        new BorderColorOption("Purple", "purple"),
        new BorderColorOption("Pink", "pink"),
        new BorderColorOption("Orange", "orange"),
        new BorderColorOption("Green", "green"),
    };

    public IEnumerable BorderColorOptions => BorderColorOptionsInternal;


    private static readonly string[] AllowedVideoExtensions =
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm",
        ".mpg", ".mpeg", ".wmv", ".flv"
    };

    public MainWindow()
    {
        _settingsPath = GetSettingsPath();
        _settings = LoadSettingsFromDisk(_settingsPath);

        InitializeComponent();

        InitializeFontComboBox();

        UpdateTimestampEnabledState();

        // Bind the listbox to our inputs collection
        InputListBox.ItemsSource = _inputs;

        // Dirty tracking: collection changes always mean "not exported".
        _inputs.CollectionChanged += (_, __) => MarkDirty();

        InitializeAdvancedSettingsFromSettings(); 

        this.Opened += MainWindow_Opened;

        // Graceful quit prompt
        this.Closing += MainWindow_Closing;
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        // Only run once
        this.Opened -= MainWindow_Opened;

        await CheckAndPromptForToolsAsync();
    }

    // ------------- Input list handlers -------------

    private async void AddInput_OnClick(object? sender, RoutedEventArgs e)
    {
        // Save current input before changing list
        SaveCurrentInputFromForm();

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select input video(s)",
                AllowMultiple = true
            });

        if (files.Count == 0)
            return;

        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            if (!IsSupportedVideoFile(path))
            {
                AppendStatus($"Skipping unsupported file type: {Path.GetFileName(path)}");
                continue;
            }

            var input = CreateInputFromCurrentForm(path);

            // NEW: get metadata + prefill timestamp UI
            await PopulateMetadataAndTimestampDefaultsAsync(input);

            _inputs.Add(input);
        }

        if (_inputs.Count > 0)
        {
            _currentInputIndex = _inputs.Count - 1;
            InputListBox.SelectedIndex = _currentInputIndex;
            LoadInputToForm(_currentInputIndex);
        }

        UpdateTimestampEnabledState();
        UpdateAddSubtitleButtonVisibility();
    }

    private void RemoveInput_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        SaveCurrentInputFromForm();

        _inputs.RemoveAt(_currentInputIndex);

        if (_inputs.Count == 0)
        {
            _currentInputIndex = -1;
            ClearInputForm();
            return;
        }

        // Select the previous item if possible, else the first
        if (_currentInputIndex >= _inputs.Count)
            _currentInputIndex = _inputs.Count - 1;

        InputListBox.SelectedIndex = _currentInputIndex;
        LoadInputToForm(_currentInputIndex);

        UpdateTimestampEnabledState();
        UpdateAddSubtitleButtonVisibility();
    }

    private void MoveInputUp_OnClick(object? sender, RoutedEventArgs e)
        => MoveInput(-1);

    private void MoveInputDown_OnClick(object? sender, RoutedEventArgs e)
        => MoveInput(1);

    private void MoveInput(int direction)
    {
        if (_inputs.Count < 2)
            return;

        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        var newIndex = _currentInputIndex + direction;

        // Ensure new index is in bounds
        if (newIndex < 0 || newIndex >= _inputs.Count)
            return;

        SaveCurrentInputFromForm();

        var item = _inputs[_currentInputIndex];
        _inputs.RemoveAt(_currentInputIndex);

        // After removal, if we removed an earlier item and moved down,
        // the target index shifts by -1, but since newIndex is based
        // on original index and we removed that item, Insert at newIndex
        // is still valid (collection size decreased by 1).
        _inputs.Insert(newIndex, item);

        _currentInputIndex = newIndex;
        InputListBox.SelectedIndex = _currentInputIndex;
        LoadInputToForm(_currentInputIndex);
        UpdateAddSubtitleButtonVisibility();
    }


    private void InputListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var newIndex = InputListBox.SelectedIndex;
        if (newIndex == _currentInputIndex)
            return;

        // Save current
        SaveCurrentInputFromForm();

        _currentInputIndex = newIndex;
        if (_currentInputIndex >= 0 && _currentInputIndex < _inputs.Count)
        {
            LoadInputToForm(_currentInputIndex);
        }
        else
        {
            ClearInputForm();
        }
        UpdateAddSubtitleButtonVisibility();
    }

    private void SubtitleFontComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.DataContext is not SubtitleSettings s) return;

        if (cb.SelectedItem is FontOption fo){
            s.Font.SelectedFontOption = fo;
            s.Font.FontFile = fo.Path;
        }
    }

    private void SubtitleBorderColorComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.DataContext is not SubtitleSettings s) return;

        if (cb.SelectedItem is BorderColorOption opt)
        {
            s.Font.SelectedBorderColorOption = opt;
            s.Font.BorderColor = opt.Value;

            // Keep width consistent
            if (opt.Value is null)
                s.Font.BorderWidth = null;
            else if (s.Font.BorderWidth is null)
                s.Font.BorderWidth = 2;
        }
    }

    private void ResetTimestamp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        var input = _inputs[_currentInputIndex];

        // If we somehow never set MetadataCreationTime, fall back to Unix epoch
        if (input.MetadataCreationTime == null)
        {
            input.MetadataCreationTime = DateTimeOffset.UnixEpoch;
            AppendStatus("[timestamp] No MetadataCreationTime set; using Unix epoch.");
        }

        var dto = input.MetadataCreationTime.Value;

        // Stay consistent with your existing code: if you used dto directly there,
        // use dto here too. If you prefer local time, change to dto.ToLocalTime().
        var local = dto;

        // Update per-input timestamp properties
        input.TimestampMonth  = local.Month.ToString("00");
        input.TimestampDay    = local.Day.ToString("00");
        input.TimestampYear   = local.Year.ToString("0000");

        int hour12 = local.Hour % 12;
        if (hour12 == 0) hour12 = 12;

        input.TimestampHour   = hour12.ToString("00");
        input.TimestampMinute = local.Minute.ToString("00");
        input.TimestampSecond = local.Second.ToString("00");
        input.TimestampAmPm   = local.Hour >= 12 ? "PM" : "AM";

        // Push values into the UI controls
        TimestampMonthTextBox.Value  = ParseIntOrDefault(input.TimestampMonth,1);
        TimestampDayTextBox.Value    = ParseIntOrDefault(input.TimestampDay,1);
        TimestampYearTextBox.Value   = ParseIntOrDefault(input.TimestampYear,1970);
        TimestampHourTextBox.Value   = ParseIntOrDefault(input.TimestampHour,12);
        TimestampMinuteTextBox.Value = ParseIntOrDefault(input.TimestampMinute,0);
        TimestampSecondTextBox.Value = ParseIntOrDefault(input.TimestampSecond,0);

        TimestampAmPmComboBox.SelectedIndex = input.TimestampAmPm == "AM" ? 0 : 1;
        

        AppendStatus("[timestamp] Timestamp reset from MetadataCreationTime.");
    }

    private SubtitleSettings CreateSubtitleFromCurrentDefaults()
    {
        int fontSize   = ParseIntOrDefault(FontSizeTextBox.Value, 32);
        int borderW    = ParseIntOrDefault(FontBorderWidthTextBox.Value, 2);
        int xPad       = ParseIntOrDefault(XPadTextBox.Value, 5);
        int yPad       = ParseIntOrDefault(YPadTextBox.Value, 5);
        int xOffset    = ParseIntOrDefault(XOffsetTextBox.Value, 0);
        int yOffset    = ParseIntOrDefault(YOffsetTextBox.Value, 0);

        var bcOpt = FontBorderColorTextBox.SelectedItem as BorderColorOption;
        string? borderColor = bcOpt?.Value; // null => no border

        var anchor = (AnchorComboBox.SelectedItem as string) ?? "bottomRight";

        var subtitle = new SubtitleSettings
        {
            Text = string.Empty,
            Start = 0,
            Duration = 2,

            InAnimation = "None",
            InAnimationDuration = 0.5,
            OutAnimation = "None",
            OutAnimationDuration = 0.5,

            Font = new FontSettings
            {
                FontFile = (FontFileComboBox.SelectedItem as FontOption)?.Path ?? GetSelectedFontPathOrDefault(),
                Size = fontSize,
                Color = FontColorTextBox.SelectedItem?.ToString() ?? "white",
                BorderColor = borderColor,
                BorderWidth = borderColor is null ? null : borderW
            },
            Position = new PositionSettings
            {
                Anchor = anchor,
                XPad = xPad,
                YPad = yPad,
                XOffset = xOffset,
                YOffset = yOffset
            }
        };

        subtitle.Font.SelectedFontOption = _fontOptions.FirstOrDefault(f => f.Path == subtitle.Font.FontFile);
        subtitle.Font.SelectedBorderColorOption =
            BorderColorOptionsInternal.FirstOrDefault(o => o.Value == subtitle.Font.BorderColor)
            ?? BorderColorOptionsInternal[0];

        return subtitle;
    }

    private static void ReindexSubtitles(System.Collections.ObjectModel.ObservableCollection<SubtitleSettings> subtitles)
    {
        for (int i = 0; i < subtitles.Count; i++)
        {
            subtitles[i].Index = i;
            subtitles[i].IsFirst = (i == 0);
            subtitles[i].IsLast = (i == subtitles.Count - 1);
        }
    }

    private void AddSubtitle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        var input = _inputs[_currentInputIndex];

        if (input.Subtitles == null)
            input.Subtitles = new System.Collections.ObjectModel.ObservableCollection<SubtitleSettings>();

        var subtitle = CreateSubtitleFromCurrentDefaults();
        input.Subtitles.Add(subtitle);

        SubtitlesItemsControl.ItemsSource = input.Subtitles;
        ReindexSubtitles(input.Subtitles);
        UpdateAddSubtitleButtonVisibility();

        MarkDirty();
    }

    private void Subtitle_AddBelow_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        if (sender is not Control button || button.DataContext is not SubtitleSettings current)
            return;

        var input = _inputs[_currentInputIndex];
        if (input.Subtitles == null)
            input.Subtitles = new System.Collections.ObjectModel.ObservableCollection<SubtitleSettings>();

        var list = input.Subtitles;
        var index = list.IndexOf(current);
        if (index < 0) index = list.Count - 1;

        var newSubtitle = CreateSubtitleFromCurrentDefaults();
        list.Insert(index + 1, newSubtitle);

        ReindexSubtitles(list);

        MarkDirty();
    }

    private void Subtitle_Remove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        if (sender is not Control button || button.DataContext is not SubtitleSettings current)
            return;

        var input = _inputs[_currentInputIndex];
        if (input.Subtitles == null)
            return;

        var list = input.Subtitles;
        if (!list.Contains(current))
            return;

        list.Remove(current);
        ReindexSubtitles(list);
        UpdateAddSubtitleButtonVisibility();

        MarkDirty();
    }

    private void Subtitle_MoveUp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        if (sender is not Control button || button.DataContext is not SubtitleSettings current)
            return;

        var input = _inputs[_currentInputIndex];
        if (input.Subtitles == null)
            return;

        var list = input.Subtitles;
        var index = list.IndexOf(current);
        if (index <= 0)
            return;

        list.Move(index, index - 1);
        ReindexSubtitles(list);

        MarkDirty();
    }

    private void Subtitle_MoveDown_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        if (sender is not Control button || button.DataContext is not SubtitleSettings current)
            return;

        var input = _inputs[_currentInputIndex];
        if (input.Subtitles == null)
            return;

        var list = input.Subtitles;
        var index = list.IndexOf(current);
        if (index < 0 || index >= list.Count - 1)
            return;

        list.Move(index, index + 1);
        ReindexSubtitles(list);

        MarkDirty();
    }

    private void HydrateSubtitleSelections(InputSettings input)
    {
        if(input.Subtitles == null) {
            return;
        }
        foreach (var s in input.Subtitles)
        {
            s.Font ??= new FontSettings();

            // Font
            var fontPath = s.Font.FontFile;
            var fontOption = !string.IsNullOrWhiteSpace(fontPath)
                ? FindFontByPath(fontPath) ?? new FontOption(Path.GetFileNameWithoutExtension(fontPath), fontPath)
                : FindFontByName("Arial") ?? _fontOptions.FirstOrDefault();

            if (fontOption != null && !_fontOptions.Contains(fontOption))
            {
                _fontOptions.Add(fontOption);
            }

            s.Font.SelectedFontOption = fontOption;

            // Border color
            var desired = s.Font.BorderColor; // string? (null allowed)
            s.Font.SelectedBorderColorOption =
                BorderColorOptionsInternal.FirstOrDefault(o => o.Value == desired)
                ?? BorderColorOptionsInternal[0];
        }
    }
/*
private FontOption? FindFontByName(string name) =>
    _fontOptions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

*/
    //--- Advanced Settings Tool Path On Clicks ---
    private async void BrowseCli_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select VideoStamper.Cli executable",
                AllowMultiple = false
            });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        if (!IsValidCliPath(path))
        {
            AppendStatus("Please select 'VideoStamper.Cli' or 'VideoStamper.Cli.exe'.");
            return;
        }

        CliPathTextBox.Text = path;
        _settings.CliPath = path;
        SaveSettingsToDisk(_settingsPath, _settings);
    }

    private async void BrowseFfmpeg_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select ffmpeg executable",
                AllowMultiple = false
            });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        if (!IsValidFfmpegPath(path))
        {
            AppendStatus("Please select 'ffmpeg' or 'ffmpeg.exe'.");
            return;
        }

        FfmpegPathTextBox.Text = path;
        _settings.FfmpegPath = path;
        SaveSettingsToDisk(_settingsPath, _settings);
    }

    private async void BrowseFfprobe_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select ffprobe executable",
                AllowMultiple = false
            });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        if (!IsValidFfprobePath(path))
        {
            AppendStatus("Please select 'ffprobe' or 'ffprobe.exe'.");
            return;
        }

        FfprobePathTextBox.Text = path;
        _settings.FfprobePath = path;
        SaveSettingsToDisk(_settingsPath, _settings);
    }

    // ------------- Importer/Exporter ------

    private async void ExportProject_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportProjectAsync(showMessages: true);
    }

    private async Task<bool> ExportProjectAsync(bool showMessages)
    {
        if (showMessages)
            OutputTextBox.Text = "";

        if (_inputs.Count == 0)
        {
            if (showMessages)
                OutputTextBox.Text = "Please add at least one input video.";
            return false;
        }

        try
        {
            // Keep model in sync with UI
            SaveCurrentInputFromForm();
            UpdateTimestampOffsetsFromTargetTime();

            var project = BuildProjectFromForm();
            NormalizeSubtitleFontsToPaths(project);

            // Suggested filename
            var firstInputPath = ProjectNameTextBox.Text ?? project.Inputs[0].Path;
            var baseName = project.Name ?? Path.GetFileNameWithoutExtension(firstInputPath);
            var safeName = SanitizeFileName(baseName);

            var save = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export VideoStamper project JSON",
                    SuggestedFileName = safeName + ".json",
                    DefaultExtension = "json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("VideoStamper Project (*.json)")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    }
                });

            if (save is null)
            {
                // user cancelled
                if (showMessages)
                    OutputTextBox.Text = "Export canceled.";
                return false;
            }

            var json = JsonSerializer.Serialize(project, _jsonOptions);
            await File.WriteAllTextAsync(save.Path.LocalPath, json);

            // Export is our "saved" point
            _isDirty = false;

            if (showMessages)
                OutputTextBox.Text = $"Exported project JSON to:\n{save.Path.LocalPath}\n";

            return true;
        }
        catch (Exception ex)
        {
            if (showMessages)
                OutputTextBox.Text = "ERROR exporting project:\n" + ex;
            return false;
        }
    }

    private async void ImportProject_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputTextBox.Text = "";

        try
        {
            // Save current input (in case user cancels import later, we don't lose edits unexpectedly)
            SaveCurrentInputFromForm();

            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import VideoStamper project JSON",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("VideoStamper Project (*.json)")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    }
                });

            if (files.Count == 0)
                return; // cancelled

            var jsonPath = files[0].Path.LocalPath;
            var json = await File.ReadAllTextAsync(jsonPath);

            var project = JsonSerializer.Deserialize<VideoStamperProject>(json, _jsonOptions);
            if (project == null)
                throw new InvalidOperationException("Could not parse project JSON.");

            project.Inputs ??= new List<InputSettings>();
            if (project.Inputs.Count == 0)
                throw new InvalidOperationException("Project contains no inputs.");

            // Resolve missing/invalid input paths
            for (int i = 0; i < project.Inputs.Count; i++)
            {
                var input = project.Inputs[i];
                var path = input.Path?.Trim();

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsSupportedVideoFile(path))
                {
                    var replacement = await PromptForReplacementVideoAsync(path);
                    if (replacement == null)
                        throw new OperationCanceledException("Import canceled while selecting a replacement input video.");

                    input.Path = replacement;
                }

                // If a project JSON was created elsewhere, metadata might be missing.
                // Populate if needed so your timestamp reset/etc doesn’t break.
                if (input.MetadataCreationTime == null)
                {
                    await PopulateMetadataAndTimestampDefaultsAsync(input);
                }

                // Ensure subtitles collection not null for UI binding
                input.Subtitles ??= new ObservableCollection<SubtitleSettings>();
            }

            // Push project settings into the form
            ProjectNameTextBox.Text = project.Name ?? "";

            if (project.Output != null)
            {
                OutputModeComboBox.SelectedIndex =
                    (project.Output.Mode ?? "separate") switch
                    {
                        "concat" => 1,
                        "concatenate" => 1,
                        _ => 0
                    };

                OutputFormatComboBox.SelectedItem =
                    (project.Output.Format ?? "mp4") switch
                    {
                        "webm" => "webm",
                        "gif" => "gif",
                        _ => "mp4"
                    };
            }

            // Tool overrides into advanced settings (and persist next launch, per your existing behavior)
            FfmpegPathTextBox.Text = project.Tools?.FfmpegPath ?? "";
            FfprobePathTextBox.Text = project.Tools?.FfprobePath ?? "";

            _settings.FfmpegPath = string.IsNullOrWhiteSpace(project.Tools?.FfmpegPath) ? null : project.Tools!.FfmpegPath;
            _settings.FfprobePath = string.IsNullOrWhiteSpace(project.Tools?.FfprobePath) ? null : project.Tools!.FfprobePath;
            SaveSettingsToDisk(_settingsPath, _settings);

            // Replace current inputs list
            _inputs.Clear();
            foreach (var input in project.Inputs)
                _inputs.Add(input);

            // Select first item and load into form
            _currentInputIndex = 0;
            InputListBox.SelectedIndex = 0;
            LoadInputToForm(0);

            UpdateTimestampEnabledState();
            UpdateAddSubtitleButtonVisibility();

            // Imported content is not yet exported in this session.
            _isDirty = true;

            OutputTextBox.Text = $"Imported project:\n{jsonPath}\n";
        }
        catch (OperationCanceledException)
        {
            OutputTextBox.Text = "Import canceled.";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = "ERROR importing project:\n" + ex;
        }
    }

    private async Task<string?> PromptForReplacementVideoAsync(string? missingPath)
    {
        var missingName = string.IsNullOrWhiteSpace(missingPath)
            ? "(missing path)"
            : Path.GetFileName(missingPath);

        while (true)
        {
            AppendStatus($"Input not found/invalid: {missingName}\nPlease select a replacement video file.");

            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = $"Select replacement for: {missingName}",
                    AllowMultiple = false
                });

            if (files.Count == 0)
                return null; // user cancelled

            var candidate = files[0].Path.LocalPath;

            if (!File.Exists(candidate))
            {
                AppendStatus("That file does not exist. Try again.");
                continue;
            }

            if (!IsSupportedVideoFile(candidate))
            {
                AppendStatus($"Unsupported file type: {Path.GetFileName(candidate)}");
                continue;
            }

            return candidate;
        }
    }


    // ------------- Run button -------------

    private async void Run_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputTextBox.Text = "";

        if (_inputs.Count == 0)
        {
            OutputTextBox.Text = "Please add at least one input video.";
            return;
        }

        if (!ValidateBeforeRun(out var errorText))
        {
            OutputTextBox.Text = errorText;
            return;
        }

        SaveCurrentInputFromForm();
        UpdateTimestampOffsetsFromTargetTime();

        ProcessingWindow? processingWindow = null;
        CancellationTokenSource? cts = null;
        string? projectPath = null;

        try
        {
            var project = BuildProjectFromForm();

            // if you rely on names in subtitles, normalize to real paths
            NormalizeSubtitleFontsToPaths(project);

            // Save JSON next to the FIRST input video (same as today)
            var firstInputPath = project.Inputs[0].Path;
            var projectDir = Path.GetDirectoryName(firstInputPath)!;

            var baseName = project.Name ?? Path.GetFileNameWithoutExtension(firstInputPath);
            var safeName = SanitizeFileName(baseName);
            projectPath = Path.Combine(
                Path.GetTempPath(),
                $"VideoStamper-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            );

            var json = JsonSerializer.Serialize(project, _jsonOptions);
            await File.WriteAllTextAsync(projectPath, json);

            OutputTextBox.Text = $"Saved project JSON to:\n{projectPath}\n\nRunning VideoStamper.Core...\n\n";

            // Show modal progress window
            processingWindow = new ProcessingWindow();
            processingWindow.SetStatus("Processing...");
            processingWindow.AppendLine("Starting...");

            cts = new CancellationTokenSource();
            processingWindow.CancelRequested += (_, __) =>
            {
                processingWindow.AppendLine("Cancel requested...");
                cts.Cancel();
            };

            processingWindow.Show(this);

            // Pipe Core progress into the progress window
            var progress = new Progress<string>(line =>
            {
                processingWindow.AppendLine(line);
            });

            var debugLevel = (DebugLevelComboBox?.SelectedItem?.ToString());
            if (string.IsNullOrWhiteSpace(debugLevel))
                debugLevel = "None";

            _isProcessing = true;

            // Run the Core processor directly
            var result = await VideoStamper.Core.ProjectProcessor.ProcessProjectAsync(
                json,
                projectPath,
                cts.Token,
                progress,
                debugLevel
            );

            processingWindow.AppendLine(result.Message);

            // Close progress window and show result window (your existing UX)
            if(debugLevel != "None") 
            {
                processingWindow.Close();
            }

            await HandleRunFinishedAsync(result.Message);

            var post = await ShowPostRunPromptAsync();
            if (post == PostRunPromptResult.QuitWithoutSaving)
            {
                _suppressClosePrompt = true;
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            processingWindow?.AppendLine("Canceled.");
            processingWindow?.Close();

            await HandleRunFinishedAsync("Canceled.");

            var post = await ShowPostRunPromptAsync();
            if (post == PostRunPromptResult.QuitWithoutSaving)
            {
                _suppressClosePrompt = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            processingWindow?.AppendLine("ERROR:");
            processingWindow?.AppendLine(ex.ToString());
            processingWindow?.Close();

            OutputTextBox.Text += "\nERROR:\n" + ex;
        }
        finally
        {
            _isProcessing = false;
            cts?.Dispose();
            try
            {
                if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
                    File.Delete(projectPath);
            }
            catch { /* ignore */ }
        }
    }


    private async void AddCustomFontButton_OnClick(object? sender, RoutedEventArgs e)
    {

        // User chose "Add Custom Font"
        var newPath = await ShowAddCustomFontDialogAsync();

        if (string.IsNullOrWhiteSpace(newPath))
        {
            // User cancelled: revert to Arial as requested
            SelectDefaultFont(preferredPath: null, preferredName: "Arial");
            return;
        }

        var newOption = AddOrGetCustomFontOption(newPath);
        FontFileComboBox.SelectedItem = newOption;
    }

    private async Task<string?> ShowAddCustomFontDialogAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select custom font (.ttf)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TrueType font (*.ttf)")
                {
                    Patterns = new[] { "*.ttf", "*.TTF" }
                }
            }
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return null; // user cancelled

        var sourcePath = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;

        var customDir = GetCustomFontsDirectory(createIfMissing: true);
        if (string.IsNullOrWhiteSpace(customDir))
            return sourcePath; // fall back to absolute path without copy

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(customDir, fileName);

        try
        {
            if (!File.Exists(destPath))
            {
                File.Copy(sourcePath, destPath);
            }
            // If it already exists, just use the existing one
        }
        catch
        {
            // If copy fails, just use original path
            return sourcePath;
        }

        return destPath;
    }

    // ------------- Build project JSON from inputs -------------

    private VideoStamperProject BuildProjectFromForm()
    {
        // Validate all inputs
        foreach (var input in _inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Path))
                throw new InvalidOperationException("One or more inputs have an empty path.");

            if (!File.Exists(input.Path))
                throw new InvalidOperationException($"Input file does not exist: {input.Path}");

            if (!IsSupportedVideoFile(input.Path))
                throw new InvalidOperationException($"Unsupported input file type: {input.Path}");
        }

        var mode = (OutputModeComboBox.SelectedItem as string)?.ToLower() ?? "separate";
        var format = (OutputFormatComboBox.SelectedItem as string) ?? "mp4";

        // Project name (optional)
        var rawName = ProjectNameTextBox.Text?.Trim();
        string? projectName = string.IsNullOrWhiteSpace(rawName)
            ? null
            : SanitizeFileName(rawName);

        // Advanced tool paths (optional overrides)
        string? ffmpegPath = string.IsNullOrWhiteSpace(FfmpegPathTextBox.Text)
            ? null
            : FfmpegPathTextBox.Text.Trim();

        if (ffmpegPath != null && !IsValidFfmpegPath(ffmpegPath))
            throw new InvalidOperationException(
                "FFmpeg path must point to 'ffmpeg' or 'ffmpeg.exe'.");

        string? ffprobePath = string.IsNullOrWhiteSpace(FfprobePathTextBox.Text)
            ? null
            : FfprobePathTextBox.Text.Trim();

        if (ffprobePath != null && !IsValidFfprobePath(ffprobePath))
            throw new InvalidOperationException(
                "ffprobe path must point to 'ffprobe' or 'ffprobe.exe'.");

        // Persist these overrides so they come back next launch
        _settings.FfmpegPath = ffmpegPath;
        _settings.FfprobePath = ffprobePath;
        SaveSettingsToDisk(_settingsPath, _settings);

        var project = new VideoStamperProject
        {
            Name = projectName,
            Tools = (ffmpegPath == null && ffprobePath == null)
                ? null
                : new ToolSettings
                {
                    FfmpegPath = ffmpegPath,
                    FfprobePath = ffprobePath
                },
            Output = new OutputSettings
            {
                Mode = mode,
                Format = format
            },
            Inputs = _inputs.ToList()
        };

        return project;
    }

    // ------------- Per-input form <-> model sync -------------

    private InputSettings CreateInputFromCurrentForm(string path)
    {

        int fontSize = ParseIntOrDefault(FontSizeTextBox.Value, 32);
        int xPad = ParseIntOrDefault(XPadTextBox.Value, 5);
        int yPad = ParseIntOrDefault(YPadTextBox.Value, 5);
        int xOffset = ParseIntOrDefault(XOffsetTextBox.Value, 0);
        int yOffset = ParseIntOrDefault(YOffsetTextBox.Value, 0);

        /*string borderColor = string.IsNullOrWhiteSpace(FontBorderColorTextBox.Text)
            ? "black"
            : FontBorderColorTextBox.Text.Trim(); */

        var bcOpt = FontBorderColorTextBox.SelectedItem as BorderColorOption;
        string? borderColor = bcOpt?.Value; // null => no border

        int borderW = ParseIntOrDefault(FontBorderWidthTextBox.Value, 2);

        var input = new InputSettings
        {
            Path = path,
            AutomaticallyFixOverlappingText = AutoFixOverlapCheckBox.IsChecked ?? true
        };

        if (TimestampEnabledCheckBox.IsChecked == true)
        {
            input.Timestamp = new TimestampSettings
            {
                Enabled = true,
                UseMetadataCreationTime = UseMetadataCheckBox.IsChecked ?? true,
                TimeOffset = 0,
                Format = TimestampFormatTextBox.Text?.Trim() ?? "yyyy-MM-dd HH:mm:ss",

                Font = new FontSettings
                {
                    FontFile = GetSelectedFontPathOrDefault(),
                    Size = fontSize,
                    Color = FontColorTextBox.Text?.Trim() ?? "white",
                    BorderColor = borderColor != "No Border" ? borderColor : "No Border",
                    BorderWidth = borderColor != "No Border" ? borderW : 2
                },
                Position = new PositionSettings
                {
                    Anchor = (AnchorComboBox.SelectedItem as string) ?? "bottomRight",
                    XPad = xPad,
                    YPad = yPad,
                    XOffset = xOffset,
                    YOffset = yOffset
                }
            };
        } else {
             TimestampEnabledCheckBox.IsChecked = false;
             input.Timestamp = new TimestampSettings
             {
                Enabled = false,
                UseMetadataCreationTime = UseMetadataCheckBox.IsChecked ?? true,
                TimeOffset = 0,
                Format = TimestampFormatTextBox.Text?.Trim() ?? "yyyy-MM-dd HH:mm:ss",

                Font = new FontSettings
                {
                    FontFile = GetSelectedFontPathOrDefault(),
                    Size = fontSize,
                    Color = FontColorTextBox.SelectedItem?.ToString() ?? "white",
                    BorderColor = borderColor != "No Border" ? borderColor : "No Border",
                    BorderWidth = borderColor != "No Border" ? borderW : 2
                },
                Position = new PositionSettings
                {
                    Anchor = (AnchorComboBox.SelectedItem as string) ?? "bottomRight",
                    XPad = xPad,
                    YPad = yPad,
                    XOffset = xOffset,
                    YOffset = yOffset
                }
             };
        }
        input.TimestampMonth   = (TimestampMonthTextBox.Value.ToString()) ?? "00";
        input.TimestampDay     = (TimestampDayTextBox.Value.ToString()) ?? "00";
        input.TimestampYear    = (TimestampYearTextBox.Value.ToString()) ?? "0000";
        input.TimestampHour    = (TimestampHourTextBox.Value.ToString()) ?? "00";
        input.TimestampMinute  = (TimestampMinuteTextBox.Value.ToString()) ?? "00";
        input.TimestampSecond  = (TimestampSecondTextBox.Value.ToString()) ?? "00";
        input.TimestampAmPm    = (TimestampAmPmComboBox.SelectedItem as string) ?? "AM";

        return input;
    }

    private void SaveCurrentInputFromForm()
    {
        if (_currentInputIndex < 0 || _currentInputIndex >= _inputs.Count)
            return;

        var input = _inputs[_currentInputIndex];

        input.Path = InputPathTextBox.Text?.Trim() ?? string.Empty;
        input.AutomaticallyFixOverlappingText = AutoFixOverlapCheckBox.IsChecked ?? true;

        int fontSize   = ParseIntOrDefault(FontSizeTextBox.Value, 32);
        int borderW    = ParseIntOrDefault(FontBorderWidthTextBox.Value, 2);
        int xPad       = ParseIntOrDefault(XPadTextBox.Value, 5);
        int yPad       = ParseIntOrDefault(YPadTextBox.Value, 5);
        int xOffset    = ParseIntOrDefault(XOffsetTextBox.Value, 0);
        int yOffset    = ParseIntOrDefault(YOffsetTextBox.Value, 0);

        /*
        string borderColor = string.IsNullOrWhiteSpace(FontBorderColorTextBox.Text)
            ? "black"
            : FontBorderColorTextBox.Text.Trim();
        */
        if (TimestampEnabledCheckBox.IsChecked == true)
        {
            input.Timestamp ??= new TimestampSettings();

            input.Timestamp.Enabled = true;
            input.Timestamp.UseMetadataCreationTime = UseMetadataCheckBox.IsChecked ?? true;
            input.Timestamp.TimeOffset = input.Timestamp.TimeOffset;
            input.Timestamp.Format = TimestampFormatTextBox.Text?.Trim() ?? "yyyy-MM-dd HH:mm:ss";

            input.Timestamp.Font ??= new FontSettings();
            input.Timestamp.Font ??= new FontSettings();
            input.Timestamp.Font.FontFile = GetSelectedFontPathOrDefault();

            input.Timestamp.Font.Size = fontSize;
            input.Timestamp.Font.Color = (FontColorTextBox.SelectedItem as string) ?? "White";

            /*
            var bc = (FontBorderColorTextBox.SelectedItem as string) ?? "No Border";
            if (string.Equals(bc, "No Border", StringComparison.OrdinalIgnoreCase))
            {
                input.Timestamp.Font.BorderColor = null;
                input.Timestamp.Font.BorderWidth = null;
            }
            else
            {
                input.Timestamp.Font.BorderColor = bc;
                input.Timestamp.Font.BorderWidth = borderW;
            }
            */

            var bcOpt = FontBorderColorTextBox.SelectedItem as BorderColorOption;
            string? borderColor = bcOpt?.Value; // null => no border

            input.Timestamp.Position ??= new PositionSettings();
            input.Timestamp.Position.Anchor = (AnchorComboBox.SelectedItem as string) ?? "bottomRight";
            input.Timestamp.Position.XPad = xPad;
            input.Timestamp.Position.YPad = yPad;
            input.Timestamp.Position.XOffset = xOffset;
            input.Timestamp.Position.YOffset = yOffset;

            input.TimestampMonth   = (TimestampMonthTextBox.Value.ToString()) ?? "00";
            input.TimestampDay     = (TimestampDayTextBox.Value.ToString()) ?? "00";
            input.TimestampYear    = (TimestampYearTextBox.Value.ToString()) ?? "0000";
            input.TimestampHour    = (TimestampHourTextBox.Value.ToString()) ?? "00";
            input.TimestampMinute  = (TimestampMinuteTextBox.Value.ToString()) ?? "00";
            input.TimestampSecond  = (TimestampSecondTextBox.Value.ToString()) ?? "00";
            input.TimestampAmPm    = (TimestampAmPmComboBox.SelectedItem as string) ?? "AM";
        }
        else
        {
            input.Timestamp ??= new TimestampSettings();
            input.Timestamp.Enabled = false;

            // keep these persisted so they stay when re-enabled
            input.Timestamp.UseMetadataCreationTime = UseMetadataCheckBox.IsChecked ?? true;
            input.Timestamp.Format = TimestampFormatTextBox.Text?.Trim() ?? "yyyy-MM-dd HH:mm:ss";

            input.Timestamp.Font ??= new FontSettings();
            input.Timestamp.Font.FontFile = GetSelectedFontPathOrDefault();
            input.Timestamp.Font.Size = fontSize;
            input.Timestamp.Font.Color = (FontColorTextBox.SelectedItem as string) ?? "White";
            
            /*
            var bc = (FontBorderColorTextBox.SelectedItem as string) ?? FontBorderColorTextBox.Text?.Trim() ?? "No Border";
            if (string.Equals(bc, "No Border", StringComparison.OrdinalIgnoreCase))
            {
                input.Timestamp.Font.BorderColor = null;
                input.Timestamp.Font.BorderWidth = null;
            }
            else
            {
                input.Timestamp.Font.BorderColor = bc;
                input.Timestamp.Font.BorderWidth = borderW;
            }
            */

            input.Timestamp.Position ??= new PositionSettings();
            input.Timestamp.Position.Anchor = (AnchorComboBox.SelectedItem as string) ?? "bottomRight";
            input.Timestamp.Position.XPad = xPad;
            input.Timestamp.Position.YPad = yPad;
            input.Timestamp.Position.XOffset = xOffset;
            input.Timestamp.Position.YOffset = yOffset;

            input.TimestampMonth   = (TimestampMonthTextBox.Value.ToString()) ?? "00";
            input.TimestampDay     = (TimestampDayTextBox.Value.ToString()) ?? "00";
            input.TimestampYear    = (TimestampYearTextBox.Value.ToString()) ?? "0000";
            input.TimestampHour    = (TimestampHourTextBox.Value.ToString()) ?? "00";
            input.TimestampMinute  = (TimestampMinuteTextBox.Value.ToString()) ?? "00";
            input.TimestampSecond  = (TimestampSecondTextBox.Value.ToString()) ?? "00";
            input.TimestampAmPm    = (TimestampAmPmComboBox.SelectedItem as string) ?? "AM";
        }

        // Conservatively mark the project as changed whenever we sync UI -> model.
        MarkDirty();

    }

    private void LoadInputToForm(int index)
    {
        if (index < 0 || index >= _inputs.Count)
            return;

        var input = _inputs[index];

        InputPathTextBox.Text = input.Path;
        AutoFixOverlapCheckBox.IsChecked = input.AutomaticallyFixOverlappingText;

        if (input.Timestamp != null)
        {
            TimestampEnabledCheckBox.IsChecked = input.Timestamp.Enabled;

            UseMetadataCheckBox.IsChecked = input.Timestamp.UseMetadataCreationTime;

            TimestampFormatTextBox.Text = input.Timestamp.Format;

            if (input.Timestamp.Font != null)
            {
                var fontPath = input.Timestamp.Font.FontFile;

                if (!string.IsNullOrWhiteSpace(fontPath))
                {
                    var existing = FindFontByPath(fontPath);

                    if (existing is null)
                    {
                        var name = Path.GetFileNameWithoutExtension(fontPath);
                        existing = new FontOption(name, fontPath);
                        _fontOptions.Insert(
                            _addCustomFontOption != null ? _fontOptions.IndexOf(_addCustomFontOption) : _fontOptions.Count,
                            existing);
                    }

                    FontFileComboBox.SelectedItem = existing;
                }
                else
                {
                    SelectDefaultFont(preferredPath: null, preferredName: "Arial");
                }

                FontSizeTextBox.Value      = input.Timestamp.Font.Size;
                FontColorTextBox.SelectedItem     = input.Timestamp.Font.Color;
                FontBorderWidthTextBox.Value = (input.Timestamp.Font.BorderWidth ?? 2);

                // set selection based on value (null => "No Border")
                var desired = input.Timestamp.Font.BorderColor; // string? (null allowed)
                FontBorderColorTextBox.SelectedItem =
                    BorderColorOptionsInternal.FirstOrDefault(o => o.Value == desired)
                    ?? BorderColorOptionsInternal[0]; // No Border

            }

            if (input.Timestamp.Position != null)
            {
                AnchorComboBox.SelectedItem = input.Timestamp.Position.Anchor;
                XPadTextBox.Value = input.Timestamp.Position.XPad;
                YPadTextBox.Value = input.Timestamp.Position.YPad;
                XOffsetTextBox.Value = input.Timestamp.Position.XOffset;
                YOffsetTextBox.Value = input.Timestamp.Position.YOffset;
            }

            // Load timestamp target UI fields
            TimestampMonthTextBox.Value  = ParseIntOrDefault(input.TimestampMonth,1);
            TimestampDayTextBox.Value    = ParseIntOrDefault(input.TimestampDay,1);
            TimestampYearTextBox.Value   = ParseIntOrDefault(input.TimestampYear,1970);
            TimestampHourTextBox.Value   = ParseIntOrDefault(input.TimestampHour,12);
            TimestampMinuteTextBox.Value = ParseIntOrDefault(input.TimestampMinute,0);
            TimestampSecondTextBox.Value = ParseIntOrDefault(input.TimestampSecond,0);

            var ampm = input.TimestampAmPm ?? "AM";
            TimestampAmPmComboBox.SelectedIndex = ampm == "AM" ? 0 : 1;

        }
        else
        {
            TimestampEnabledCheckBox.IsChecked = false;
            // Load timestamp target UI fields
            TimestampMonthTextBox.Value  = ParseIntOrDefault(input.TimestampMonth,1);
            TimestampDayTextBox.Value    = ParseIntOrDefault(input.TimestampDay,1);
            TimestampYearTextBox.Value   = ParseIntOrDefault(input.TimestampYear,1970);
            TimestampHourTextBox.Value   = ParseIntOrDefault(input.TimestampHour,12);
            TimestampMinuteTextBox.Value = ParseIntOrDefault(input.TimestampMinute,0);
            TimestampSecondTextBox.Value = ParseIntOrDefault(input.TimestampSecond,0);

            var ampm = input.TimestampAmPm ?? "AM";
            TimestampAmPmComboBox.SelectedIndex = ampm == "AM" ? 0 : 1;
        }

        // --- Subtitles ---
        if (input.Subtitles == null)
            input.Subtitles = new System.Collections.ObjectModel.ObservableCollection<SubtitleSettings>();

        HydrateSubtitleSelections(input);
        SubtitlesItemsControl.ItemsSource = input.Subtitles;
        ReindexSubtitles(input.Subtitles);
        UpdateAddSubtitleButtonVisibility();

    }

    private void ClearInputForm()
    {
        InputPathTextBox.Text = string.Empty;
        AutoFixOverlapCheckBox.IsChecked = true;
        TimestampEnabledCheckBox.IsChecked = true;
        UseMetadataCheckBox.IsChecked = true;

        TimestampFormatTextBox.Text = "yyyy-MM-dd HH:mm:ss";
        SelectDefaultFont(null, null);
        FontSizeTextBox.Value = 32;
        FontColorTextBox.SelectedItem = "White";
        AnchorComboBox.SelectedIndex = 3;
        XPadTextBox.Value = 5;
        YPadTextBox.Value = 5;
        XOffsetTextBox.Value = 0;
        YOffsetTextBox.Value = 0;

        FontColorTextBox.SelectedItem = "White";
        FontBorderWidthTextBox.Value = 2;
        FontBorderColorTextBox.SelectedItem =
        BorderColorOptionsInternal.FirstOrDefault(o => o.Value == "black")
        ?? BorderColorOptionsInternal[0];


        TimestampMonthTextBox.Value  = null;
        TimestampDayTextBox.Value    = null;
        TimestampYearTextBox.Value   = null;
        TimestampHourTextBox.Value   = null;
        TimestampMinuteTextBox.Value = null;
        TimestampSecondTextBox.Value = null;
        TimestampAmPmComboBox.SelectedItem = "AM";

        SubtitlesItemsControl.ItemsSource = null;
        UpdateAddSubtitleButtonVisibility();

    }

    // ------------- Post-run prompt -------------

    private enum PostRunChoice
    {
        Continue,
        QuitWithoutExporting
    }

    private async Task HandleRunFinishedAsync(string message)
    {
        var choice = await ShowRunCompleteDialogAsync(message);

        if (choice == PostRunChoice.QuitWithoutExporting)
        {
            _suppressClosePrompt = true;

            Close();
        }
    }

    private async Task<PostRunChoice> ShowRunCompleteDialogAsync(string message)
    {
        var tcs = new TaskCompletionSource<PostRunChoice>();

        var win = new Window
        {
            Title = "VideoStamper",
            Width = 520,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var msgBox = new TextBox
        {
            Text = message,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 200
        };

        ScrollViewer.SetVerticalScrollBarVisibility(msgBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(msgBox, ScrollBarVisibility.Disabled);

        var continueBtn = new Button { Content = "Continue", MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) };
        var quitBtn = new Button { Content = "Quit without Exporting", MinWidth = 200 };

        continueBtn.Click += (_, __) =>
        {
            tcs.TrySetResult(PostRunChoice.Continue);
            win.Close();
        };

        quitBtn.Click += (_, __) =>
        {
            tcs.TrySetResult(PostRunChoice.QuitWithoutExporting);
            win.Close();
        };

        win.Closed += (_, __) =>
        {
            // If the user closes the dialog via the window close button, treat as Continue.
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(PostRunChoice.Continue);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                continueBtn,
                quitBtn
            }
        };

        win.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Finished", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0,0,0,8) },
                msgBox,
                buttons
            }
        };

        await win.ShowDialog(this);
        return await tcs.Task;
    }

    // ------------- Process runner -------------

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunCliAsync(
        string cliPath,
        string projectJsonPath,
        string? debugArg,
        OutputWindow? liveOutputWindow)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add(projectJsonPath);
        if (debugArg != null)
            psi.ArgumentList.Add(debugArg);

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var proc = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        proc.Start();

        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
            {
                stdoutBuilder.AppendLine(line);
                if (liveOutputWindow != null)
                {
                    await liveOutputWindow.AppendLineAsync(line);
                }
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync()) != null)
            {
                stderrBuilder.AppendLine(line);
                if (liveOutputWindow != null)
                {
                    await liveOutputWindow.AppendLineAsync("[ERR] " + line);
                }
            }
        });

        await Task.WhenAll(stdoutTask, stderrTask, proc.WaitForExitAsync());

        return (stdoutBuilder.ToString(), stderrBuilder.ToString(), proc.ExitCode);
    }

    // ------------- Font helpers -------------

    private void InitializeFontComboBox()
    {
        if (FontFileComboBox is null)
            return;

        FontFileComboBox.ItemsSource = _fontOptions;
        LoadFontOptions();
        SelectDefaultFont(preferredPath: null, preferredName: "Arial");
    }

    private void LoadFontOptions()
    {
        _fontOptions.Clear();
        _addCustomFontOption = null;

        var fontMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // System font directories
        foreach (var dir in EnumerateSystemFontDirectories())
        {
            AddFontsFromDirectory(dir, fontMap, isCustom: false);
        }

        // Custom fonts directory under project root (if present)
        var customDir = GetCustomFontsDirectory(createIfMissing: false);
        if (!string.IsNullOrWhiteSpace(customDir))
        {
            AddFontsFromDirectory(customDir!, fontMap, isCustom: true);
        }

        foreach (var kvp in fontMap.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            _fontOptions.Add(new FontOption(kvp.Key, kvp.Value));
        }
    }

    private static IEnumerable<string> EnumerateSystemFontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir))
            {
                var fontsDir = Path.Combine(windowsDir, "Fonts");
                if (Directory.Exists(fontsDir))
                    yield return fontsDir;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dirs = new[]
            {
                "/System/Library/Fonts",
                "/Library/Fonts",
                Path.Combine(home, "Library", "Fonts")
            };

            foreach (var dir in dirs)
            {
                if (Directory.Exists(dir))
                    yield return dir;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dirs = new[]
            {
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                Path.Combine(home, ".fonts"),
                Path.Combine(home, ".local", "share", "fonts")
            };

            foreach (var dir in dirs)
            {
                if (Directory.Exists(dir))
                    yield return dir;
            }
        }
    }

    private static string? GetCustomFontsDirectory(bool createIfMissing)
    {
        try
        {
            var baseDir = Directory.GetCurrentDirectory();
            var fontsDir = Path.Combine(baseDir, "fonts");

            if (Directory.Exists(fontsDir))
                return fontsDir;

            if (createIfMissing)
            {
                Directory.CreateDirectory(fontsDir);
                return fontsDir;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void AddFontsFromDirectory(
        string? directory,
        IDictionary<string, string> map,
        bool isCustom)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            if (!Directory.Exists(directory))
                return;

            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".ttf", ".otf", ".ttc"
            };

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
                    continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (map.TryGetValue(name, out var existingPath))
                {
                    // Prefer custom fonts over system fonts when names collide
                    if (isCustom)
                        map[name] = file;
                }
                else
                {
                    map[name] = file;
                }
            }
        }
        catch
        {
            // Silently skip issues, per your requirement
        }
    }

    private void NormalizeSubtitleFontsToPaths(VideoStamperProject project)
    {
        foreach (var input in project.Inputs)
        {
            if (input.Subtitles == null) continue;

            foreach (var s in input.Subtitles)
            {
                var nameOrPath = s.Font?.FontFile;
                if (string.IsNullOrWhiteSpace(nameOrPath)) continue;

                // If it's already a path we know, keep it
                if (FindFontByPath(nameOrPath) != null)
                    continue;

                // Otherwise treat it like a name and map to path
                var opt = FindFontByName(nameOrPath);
                if (opt != null && s.Font != null)
                    s.Font.FontFile = opt.Path;
            }
        }
    }

    private FontOption? FindFontByName(string name)
        => _fontOptions.FirstOrDefault(f =>
            !f.IsAddCustom &&
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private FontOption? FindFontByPath(string path)
        => _fontOptions.FirstOrDefault(f =>
            !f.IsAddCustom &&
            string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    private void SelectDefaultFont(string? preferredPath, string? preferredName)
    {
        FontOption? selected = null;

        // 1) Try exact path match
        if (!string.IsNullOrWhiteSpace(preferredPath))
            selected = FindFontByPath(preferredPath);

        // 2) Try preferred name (used for Arial explicit preference)
        if (selected is null && !string.IsNullOrWhiteSpace(preferredName))
            selected = FindFontByName(preferredName);

        // 3) Prefer Arial as a universal fallback if nothing else chosen
        if (selected is null)
            selected = FindFontByName("Arial");

        // 4) Fall back to the first non-special font, if any
        if (selected is null)
            selected = _fontOptions.FirstOrDefault(f => !f.IsAddCustom);

        // 5) As an absolute last resort, select the "Add Custom Font" item
        if (selected is null && _addCustomFontOption is not null)
            selected = _addCustomFontOption;

        if (selected is not null)
            FontFileComboBox.SelectedItem = selected;
    }

    private string GetSelectedFontPathOrDefault()
    {
        if (FontFileComboBox?.SelectedItem is FontOption option && !option.IsAddCustom)
            return option.Path;

        // User might have typed a manual path in the editable ComboBox
        var text = FontFileComboBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        // Prefer Arial as default path if available
        var arial = FindFontByName("Arial");
        if (arial is not null)
            return arial.Path;

        // OS-specific fallbacks
        if (OperatingSystem.IsLinux())
            return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

        if (OperatingSystem.IsWindows())
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir))
            {
                var arialPath = Path.Combine(windowsDir, "Fonts", "arial.ttf");
                if (File.Exists(arialPath))
                    return arialPath;
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            var candidate = "/System/Library/Fonts/Supplemental/Arial.ttf";
            if (File.Exists(candidate))
                return candidate;
        }

        // Last-resort: Linux default
        return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    }

    private FontOption AddOrGetCustomFontOption(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        // If we already know this exact path, reuse it
        var existing = _fontOptions.FirstOrDefault(f =>
            !f.IsAddCustom &&
            string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            return existing;

        var newOption = new FontOption(name, path);

        if (_addCustomFontOption is not null)
        {
            var idx = _fontOptions.IndexOf(_addCustomFontOption);
            if (idx >= 0)
                _fontOptions.Insert(idx, newOption); // insert just above "Add Custom Font"
            else
                _fontOptions.Add(newOption);
        }
        else
        {
            _fontOptions.Add(newOption);
        }

        return newOption;
    }

    // ------------- Other Helpers -------------

    private void MarkDirty()
    {
        // Don't spam dirty flips while a job is running; we also don't want
        // background updates to trigger quit prompts.
        if (_isProcessing)
            return;

        _isDirty = true;
    }

    private void UpdateTimestampEnabledState()
    {
        var items = InputListBox.Items;
        bool hasInputs = items != null && items.Cast<object>().Any();

        TimestampEnabledCheckBox.IsEnabled = hasInputs;

        if (!hasInputs)
        {
            // No videos: force it off
            TimestampEnabledCheckBox.IsChecked = false;
        }
    }

    private void UpdateAddSubtitleButtonVisibility()
    {
        if (AddSubtitleButton is null)
            return;

        // Hide by default
        var show = false;

        // Show only when:
        // - there is at least 1 input
        // - a valid input is selected
        // - the selected input has 0 subtitles
        if (_inputs.Count > 0 &&
            _currentInputIndex >= 0 &&
            _currentInputIndex < _inputs.Count)
        {
            var input = _inputs[_currentInputIndex];
            var subtitleCount = input.Subtitles?.Count ?? 0;
            show = subtitleCount == 0;
        }

        AddSubtitleButton.IsVisible = show;
    }

    private void InitializeAdvancedSettingsFromSettings()
    {
        var cliPath = string.IsNullOrWhiteSpace(_settings.CliPath)
            ? GetDefaultCliPath()
            : _settings.CliPath;

        CliPathTextBox.Text = cliPath ?? string.Empty;

        var ffmpegPath = _settings.FfmpegPath ?? GetDefaultFfmpegPath();
        var ffprobePath = _settings.FfprobePath ?? GetDefaultFfprobePath();

        FfmpegPathTextBox.Text = ffmpegPath ?? string.Empty;
        FfprobePathTextBox.Text = ffprobePath ?? string.Empty;
    }

    private static int ParseIntOrDefault(string? text, int fallback)
        => int.TryParse(text, out var value) ? value : fallback;

    private static int ParseIntOrDefault(decimal? value, int fallback)
        => value is null ? fallback : (int)Math.Round(value.Value);

    private static bool IsSupportedVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return AllowedVideoExtensions.Contains(ext);
    }

    private void AppendStatus(string message)
    {
        if (!string.IsNullOrEmpty(OutputTextBox.Text))
            OutputTextBox.Text += Environment.NewLine + message;
        else
            OutputTextBox.Text = message;
    }

    private void UpdateTimestampOffsetsFromTargetTime()
    {
        foreach (var input in _inputs)
        {
            if (input.Timestamp is null || !input.Timestamp.Enabled)
                continue;

            if (TryComputeOffsetSeconds(input, out var offsetSeconds))
            {
                input.Timestamp.TimeOffset = offsetSeconds;
            }
            else
            {
                // If parsing fails or metadata missing, fall back to 0
                input.Timestamp.TimeOffset = 0;
            }
        }
    }

    private static bool TryComputeOffsetSeconds(InputSettings input, out int offsetSeconds)
    {
        offsetSeconds = 0;

        if (input.MetadataCreationTime is null)
            return false;

        if (string.IsNullOrWhiteSpace(input.TimestampYear) ||
            string.IsNullOrWhiteSpace(input.TimestampMonth) ||
            string.IsNullOrWhiteSpace(input.TimestampDay) ||
            string.IsNullOrWhiteSpace(input.TimestampHour) ||
            string.IsNullOrWhiteSpace(input.TimestampMinute) ||
            string.IsNullOrWhiteSpace(input.TimestampSecond) ||
            string.IsNullOrWhiteSpace(input.TimestampAmPm))
        {
            return false;
        }

        if (!int.TryParse(input.TimestampYear, out var year) ||
            !int.TryParse(input.TimestampMonth, out var month) ||
            !int.TryParse(input.TimestampDay, out var day) ||
            !int.TryParse(input.TimestampHour, out var hour12) ||
            !int.TryParse(input.TimestampMinute, out var minute) ||
            !int.TryParse(input.TimestampSecond, out var second))
        {
            return false;
        }

        if (month < 1 || month > 12) return false;
        if (day < 1 || day > 31) return false;   // keep it simple
        if (hour12 < 1 || hour12 > 12) return false;
        if (minute < 0 || minute > 59) return false;
        if (second < 0 || second > 59) return false;

        var isPm = input.TimestampAmPm?.Equals("PM", StringComparison.OrdinalIgnoreCase) == true;
        var hour24 = hour12 % 12;
        if (isPm) hour24 += 12;

        var targetLocal = new DateTime(year, month, day, hour24, minute, second, DateTimeKind.Local);
        var target = new DateTimeOffset(targetLocal);

        var metaLocal = input.MetadataCreationTime.Value.ToLocalTime();

        var diff = target - metaLocal;
        offsetSeconds = (int)Math.Round(diff.TotalSeconds);
        return true;
    }

    private bool ValidateBeforeRun(out string errorText)
    {
        var errors = new List<string>();

        // Validate subtitles for every input (because the project can process multiple inputs)
        for (int i = 0; i < _inputs.Count; i++)
        {
            var input = _inputs[i];
            var ts = input.Timestamp;

            if (ts is not null && ts.Enabled)
            {
                ValidateTimestamp(input, errors, prefix: $"Input {i + 1} Timestamp");
            }

            if (input.Subtitles == null || input.Subtitles.Count == 0)
                continue;

            for (int s = 0; s < input.Subtitles.Count; s++)
            {
                ValidateSubtitle(input.Subtitles[s], errors, prefix: $"Input {i + 1} / Subtitle {s + 1}");
            }
        }

        if (errors.Count > 0)
        {
            errorText = "Please fix the following issues:\n\n- " + string.Join("\n- ", errors);
            return false;
        }

        errorText = "";
        return true;
    }

    private void ValidateTimestamp(InputSettings input, List<string> errors, string prefix)
    {
        var ts = input.Timestamp;
        if (ts is null)
        {
            errors.Add($"{prefix}: Timestamp settings are missing.");
            return;
        }

        var fmt = ts.Format;
        if (string.IsNullOrEmpty(fmt))
            errors.Add($"{prefix}: Format must be at least 1 character.");

        // Month/Day/Year
        if (!TryGetInt(input.TimestampMonth, out var month))
            errors.Add($"{prefix}: Month is required.");
        else if (month < 1 || month > 12)
            errors.Add($"{prefix}: Month must be between 1 and 12.");

        if (!TryGetInt(input.TimestampYear, out var year))
            errors.Add($"{prefix}: Year is required.");

        if (!TryGetInt(input.TimestampDay, out var day))
            errors.Add($"{prefix}: Day is required.");
        else if (month is >= 1 and <= 12)
        {
            var maxDay = GetMaxDay(year, month);
            if (day < 1 || day > maxDay)
                errors.Add($"{prefix}: Day must be between 1 and {maxDay} for {month}/{year}.");
        }

        // Time
        if (!TryGetInt(input.TimestampHour, out var hour))
            errors.Add($"{prefix}: Hour is required.");
        else if (hour < 0 || hour > 23)
            errors.Add($"{prefix}: Hour must be between 0 and 23.");

        if (!TryGetInt(input.TimestampMinute, out var minute))
            errors.Add($"{prefix}: Minute is required.");
        else if (minute < 0 || minute > 59)
            errors.Add($"{prefix}: Minute must be between 0 and 59.");

        if (!TryGetInt(input.TimestampSecond, out var second))
            errors.Add($"{prefix}: Second is required.");
        else if (second < 0 || second > 59)
            errors.Add($"{prefix}: Second must be between 0 and 59.");

        // Font size > 0
        if (ts.Font == null || ts.Font.Size <= 0)
            errors.Add($"{prefix}: Font size must be greater than 0.");

        // Border width > 0 if border color is set
        if (ts.Font != null && !string.IsNullOrWhiteSpace(ts.Font.BorderColor))
        {
            if (ts.Font.BorderWidth == null || ts.Font.BorderWidth <= 0)
                errors.Add($"{prefix}: Border width must be > 0 when border color is set.");
        }

        // Pads 0..100
        if (ts.Position == null)
        {
            errors.Add($"{prefix}: Position is missing.");
            return;
        }

        if (ts.Position.XPad < 0 || ts.Position.XPad > 100)
            errors.Add($"{prefix}: X padding must be between 0 and 100.");

        if (ts.Position.YPad < 0 || ts.Position.YPad > 100)
            errors.Add($"{prefix}: Y padding must be between 0 and 100.");
    }

    private void ValidateSubtitle(SubtitleSettings sub, List<string> errors, string prefix)
    {
        // Start/Duration must be numbers
        // (If your SubtitleSettings uses double for Start/Duration, these are already numeric,
        // but this validation still enforces presence and sane values.)
        if (double.IsNaN(sub.Start) || double.IsInfinity(sub.Start))
            errors.Add($"{prefix}: Start time must be a number.");
        else if (sub.Start < 0)
            errors.Add($"{prefix}: Start time must be >= 0.");

        if (double.IsNaN(sub.Duration) || double.IsInfinity(sub.Duration))
            errors.Add($"{prefix}: Duration must be a number.");
        else if (sub.Duration <= 0)
            errors.Add($"{prefix}: Duration must be > 0.");

        // Font size > 0
        if (sub.Font == null || sub.Font.Size <= 0)
            errors.Add($"{prefix}: Font size must be greater than 0.");

        // Border width > 0 if border color is set
        if (sub.Font != null && !string.IsNullOrWhiteSpace(sub.Font.BorderColor))
        {
            if (sub.Font.BorderWidth == null || sub.Font.BorderWidth <= 0)
                errors.Add($"{prefix}: Border width must be > 0 when border color is set.");
        }

        // Pads 0..100
        if (sub.Position == null)
        {
            errors.Add($"{prefix}: Position is missing.");
            return;
        }

        if (sub.Position.XPad < 0 || sub.Position.XPad > 100)
            errors.Add($"{prefix}: X padding must be between 0 and 100.");

        if (sub.Position.YPad < 0 || sub.Position.YPad > 100)
            errors.Add($"{prefix}: Y padding must be between 0 and 100.");

        // Offsets just need to be integers; in your model they already are ints
        // so there’s nothing to parse—this is implicitly satisfied.
    }

    private static bool TryGetInt(decimal? value, out int result)
    {
        result = 0;
        if (value is null) return false;

        var v = value.Value;

        // Ensure it's an integer (no fractional component)
        if (v != decimal.Truncate(v))
            return false;

        // Ensure it fits in int
        if (v < int.MinValue || v > int.MaxValue)
            return false;

        result = (int)v;
        return true;
    }

    private static bool TryGetInt(string? value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Parse as decimal so we can detect fractional values (e.g. "1.5")
        if (!decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        return TryGetInt(parsed, out result);
    }


    private static int GetMaxDay(int year, int month)
    {
        return month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31
        };
    }

    private static bool IsLeapYear(int year)
    {
        // Gregorian leap year rules (works for any integer year value)
        if (year % 400 == 0) return true;
        if (year % 100 == 0) return false;
        return year % 4 == 0;
    }


    private static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "VideoStamper");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "settings.json");
    }

    private AppSettings LoadSettingsFromDisk(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                    return settings;
            }
        }
        catch
        {
            // Ignore and fall back to defaults
        }

        return new AppSettings();
    }

    private void SaveSettingsToDisk(string path, AppSettings settings)
    {
        try
        {
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Swallow errors; worst case the user just has to re-enter paths.
        }
    }

    private static string? GetDefaultCliPath()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                var dir = Path.GetDirectoryName(exe)!;

                // Same directory as VideoStamper.Gui
                var basePath = Path.Combine(dir, "VideoStamper.Cli");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var exePath = basePath + ".exe";
                    if (File.Exists(exePath))
                        return exePath;
                }

                if (File.Exists(basePath))
                    return basePath;
            }
        }
        catch
        {
            // ignore and fall through
        }

        // Fall back to relying on PATH / system resolution
        return "VideoStamper.Cli";
    }

    private static string GetPlatformSubfolder()
    {
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "macos-arm64"
                : "macos-x64"; // matches your current macOS build layout
        if (OperatingSystem.IsWindows())
            return "win-x64";
        if (OperatingSystem.IsLinux())
            return "linux-x64";

        return "unknown";
    }

    private static string? GetDefaultFfmpegPath()
    {
        var baseDir = Directory.GetCurrentDirectory();
        var sub = GetPlatformSubfolder();
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidate = Path.Combine(baseDir, "bin", sub, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? GetDefaultFfprobePath()
    {
        var baseDir = Directory.GetCurrentDirectory();
        var sub = GetPlatformSubfolder();
        var fileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var candidate = Path.Combine(baseDir, "bin", sub, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool IsToolFile(string path, string toolBaseName)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return false;

        var lower = name.ToLowerInvariant();
        var expected = toolBaseName.ToLowerInvariant();
        return lower == expected || lower == expected + ".exe";
    }

    private static bool IsValidCliPath(string path) => IsToolFile(path, "VideoStamper.Cli");
    private static bool IsValidFfmpegPath(string path) => IsToolFile(path, "ffmpeg");
    private static bool IsValidFfprobePath(string path) => IsToolFile(path, "ffprobe");

    // Sanitize project name to be a safe filename and prevent absurdly long paths
    private static string SanitizeFileName(string? name, int maxLength = 64)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "VideoStamperProject";

        var invalid = Path.GetInvalidFileNameChars();
        var cleanedChars = name.Where(ch => !invalid.Contains(ch)).ToArray();
        var cleaned = new string(cleanedChars).Trim();

        if (string.IsNullOrEmpty(cleaned))
            cleaned = "VideoStamperProject";

        if (cleaned.Length > maxLength)
            cleaned = cleaned.Substring(0, maxLength);

        return cleaned;
    }

    private async Task CheckAndPromptForToolsAsync()
    {
        // VideoStamper.Cli
       /* await CheckSingleToolAsync(
            displayName: "VideoStamper.Cli",
            expectedFileNames: new[] { "VideoStamper.Cli", "VideoStamper.Cli.exe" },
            getCurrentPath: () => CliPathTextBox.Text,
            resolveDefaultPath: GetDefaultCliPath,
            setTextBox: path => CliPathTextBox.Text = path ?? string.Empty,
            saveSetting: path => _settings.CliPath = path
        ); */

        // ffmpeg
        await CheckSingleToolAsync(
            displayName: "ffmpeg",
            expectedFileNames: new[] { "ffmpeg", "ffmpeg.exe" },
            getCurrentPath: () => FfmpegPathTextBox.Text,
            resolveDefaultPath: GetDefaultFfmpegPath,
            setTextBox: path => FfmpegPathTextBox.Text = path ?? string.Empty,
            saveSetting: path => _settings.FfmpegPath = path
        );

        // ffprobe
        await CheckSingleToolAsync(
            displayName: "ffprobe",
            expectedFileNames: new[] { "ffprobe", "ffprobe.exe" },
            getCurrentPath: () => FfprobePathTextBox.Text,
            resolveDefaultPath: GetDefaultFfprobePath,
            setTextBox: path => FfprobePathTextBox.Text = path ?? string.Empty,
            saveSetting: path => _settings.FfprobePath = path
        );

        // Persist whatever we ended up with
        SaveSettingsToDisk(_settingsPath, _settings);
    }

    private async Task CheckSingleToolAsync(
    string displayName,
    string[] expectedFileNames,
    Func<string?> getCurrentPath,
    Func<string?> resolveDefaultPath,
    Action<string> setTextBox,
    Action<string?> saveSetting)
    {
        // Helper: does the filename match what we expect?
        bool IsExpectedFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return expectedFileNames.Any(e =>
                string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase));
        }

        // 1. Current value from textbox
        var path = getCurrentPath()?.Trim();

        if (!string.IsNullOrWhiteSpace(path) &&
            IsExpectedFile(path) &&
            File.Exists(path))
        {
            // Looks good; store it
            saveSetting(path);
            return;
        }

        // 2. Try default path for the platform
        var defaultPath = resolveDefaultPath();
        if (!string.IsNullOrWhiteSpace(defaultPath) &&
            IsExpectedFile(defaultPath!) &&
            File.Exists(defaultPath!))
        {
            setTextBox(defaultPath!);
            saveSetting(defaultPath!);
            return;
        }

        // 3. Prompt the user with a modal file picker
        await PromptForToolAsync(displayName, path, expectedFileNames, setTextBox, saveSetting);
    }

    private async Task PromptForToolAsync(
    string displayName,
    string foundInstead,
    string[] expectedFileNames,
    Action<string> setTextBox,
    Action<string?> saveSetting)
    {
        while (true)
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = $"{displayName} was not found. Found {foundInstead} instead. Please locate its executable.",
                    AllowMultiple = false
                });

            if (files.Count == 0)
            {
                // User cancelled. Leave it unset, but warn in the log.
                AppendStatus($"{displayName} is not configured. " +
                             $"You can set it later in Advanced settings.");
                break;
            }

            var path = files[0].Path.LocalPath;
            var fileName = Path.GetFileName(path);

            // Check filename matches what we expect (e.g. ffmpeg or ffmpeg.exe)
            if (!expectedFileNames.Any(e =>
                    string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                AppendStatus(
                    $"Invalid selection for {displayName}. " +
                    $"Please choose {string.Join(" or ", expectedFileNames)}.");
                continue; // re-show picker
            }

            if (!File.Exists(path))
            {
                AppendStatus($"The selected {displayName} file does not exist: {path}");
                continue; // re-show picker
            }

            // Looks good — update UI + settings
            setTextBox(path);
            saveSetting(path);
            AppendStatus($"{displayName} path set to: {path}");
            break;
        }

        SaveSettingsToDisk(_settingsPath, _settings);
    }

    private async Task PopulateMetadataAndTimestampDefaultsAsync(InputSettings input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input.Path) || !File.Exists(input.Path))
            {
                AppendStatus($"[ffprobe] Skipping invalid path: {input.Path}");
                return;
            }

            // Determine ffprobe path (your override logic)
            var ffprobePath = string.IsNullOrWhiteSpace(FfprobePathTextBox.Text)
                ? _settings.FfprobePath
                : FfprobePathTextBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(ffprobePath))
            {
                VideoStamper.Core.FfmpegLocator.CustomFfprobePath = ffprobePath;
                AppendStatus($"[ffprobe] Using ffprobe path: {ffprobePath}");
            }

            // ---- NEW: Capture raw ffprobe JSON ----
            AppendStatus($"[ffprobe] Running ffprobe for: {input.Path}");

            var meta = await VideoStamper.Core.VideoMetadataReader.GetMetadataAsync(
                input.Path,
                CancellationToken.None
            );

            AppendStatus("[ffprobe] Parsed metadata:");
            AppendStatus($"  CreationTimeRaw = {meta.CreationTimeRaw}");
            AppendStatus($"  Width           = {meta.Width.ToString()}");
            AppendStatus($"  Height          = {meta.Height.ToString()}");
            AppendStatus($"  DurationSeconds = {meta.DurationSeconds.ToString()}");

            // ---- Save metadata to the GUI input settings ----
            DateTimeOffset dto;

            if (!string.IsNullOrWhiteSpace(meta.CreationTimeRaw) &&
                DateTimeOffset.TryParse(meta.CreationTimeRaw, out var parsed))
            {
                dto = parsed;
                AppendStatus("[ffprobe] Timestamp parsed successfully.");
            }
            else
            {
                dto = DateTimeOffset.UnixEpoch; // 1970-01-01T00:00:00Z
                input.MetadataCreationTime = dto;
                AppendStatus("[ffprobe] WARNING: No parseable creation timestamp found. Using Unix epoch.");
            }

            // Always set MetadataCreationTime
            input.MetadataCreationTime = dto;

            // Extract components (LOCAL)
            var local = dto;

            // Date
            input.TimestampMonth  = local.Month.ToString("00");
            input.TimestampDay    = local.Day.ToString("00");
            input.TimestampYear   = local.Year.ToString("0000");

            // Time (12h)
            int hour12 = local.Hour % 12;
            if (hour12 == 0) hour12 = 12;

            input.TimestampHour   = hour12.ToString("00");
            input.TimestampMinute = local.Minute.ToString("00");
            input.TimestampSecond = local.Second.ToString("00");
            input.TimestampAmPm   = local.Hour >= 12 ? "PM" : "AM";

            AppendStatus("[ffprobe] Timestamp UI populated successfully.");

        }
        catch (Exception ex)
        {
            AppendStatus("[ffprobe] EXCEPTION:");
            AppendStatus(ex.ToString());
        }
    }

    // ------------- Quit / post-run prompts -------------

    private enum ExitPromptResult
    {
        ExportAndQuit,
        QuitWithoutExporting,
        Cancel
    }

    private enum PostRunPromptResult
    {
        Continue,
        QuitWithoutSaving
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt)
            return;

        // Avoid re-entrancy (Closing can fire multiple times).
        if (_closePromptOpen)
        {
            e.Cancel = true;
            return;
        }

        // If a job is running, don't allow window close (it can orphan ffmpeg).
        if (_isProcessing)
        {
            e.Cancel = true;
            await ShowSimpleInfoDialogAsync(
                title: "VideoStamper",
                message: "A job is currently running. Please cancel or wait for it to finish before closing.");
            return;
        }

        // Nothing to prompt about
        if (!_isDirty || _inputs.Count == 0)
            return;

        e.Cancel = true;
        _closePromptOpen = true;
        try
        {
            var choice = await ShowExitPromptAsync(
                title: "Quit VideoStamper",
                message: "Do you want to export your project before quitting?");

            switch (choice)
            {
                case ExitPromptResult.ExportAndQuit:
                {
                    var exported = await ExportProjectAsync(showMessages: true);
                    if (!exported)
                        return; // user canceled export or export failed

                    _suppressClosePrompt = true;
                    Close();
                    break;
                }
                case ExitPromptResult.QuitWithoutExporting:
                    _suppressClosePrompt = true;
                    Close();
                    break;
                case ExitPromptResult.Cancel:
                default:
                    break;
            }
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    private async Task<ExitPromptResult> ShowExitPromptAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<ExitPromptResult>();

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var exportBtn = new Button { Content = "Export & Quit", MinWidth = 120 };
        var quitBtn = new Button { Content = "Quit Without Exporting", MinWidth = 160 };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90 };

        exportBtn.Click += (_, __) => { tcs.TrySetResult(ExitPromptResult.ExportAndQuit); dialog.Close(); };
        quitBtn.Click += (_, __) => { tcs.TrySetResult(ExitPromptResult.QuitWithoutExporting); dialog.Close(); };
        cancelBtn.Click += (_, __) => { tcs.TrySetResult(ExitPromptResult.Cancel); dialog.Close(); };

        dialog.Closed += (_, __) =>
        {
            // If user clicks the window X, treat as Cancel.
            tcs.TrySetResult(ExitPromptResult.Cancel);
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { exportBtn, quitBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private async Task<PostRunPromptResult> ShowPostRunPromptAsync()
    {
        var tcs = new TaskCompletionSource<PostRunPromptResult>();

        var dialog = new Window
        {
            Title = "Job Finished",
            Width = 520,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var text = new TextBlock
        {
            Text = "Your job has finished. Do you want to continue working or quit without saving?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var continueBtn = new Button { Content = "Continue", MinWidth = 110 };
        var quitBtn = new Button { Content = "Quit Without Saving", MinWidth = 160 };

        continueBtn.Click += (_, __) => { tcs.TrySetResult(PostRunPromptResult.Continue); dialog.Close(); };
        quitBtn.Click += (_, __) => { tcs.TrySetResult(PostRunPromptResult.QuitWithoutSaving); dialog.Close(); };

        dialog.Closed += (_, __) =>
        {
            // If user clicks X, default to Continue.
            tcs.TrySetResult(PostRunPromptResult.Continue);
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { continueBtn, quitBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private async Task ShowSimpleInfoDialogAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<object?>();

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var ok = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, __) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, __) => tcs.TrySetResult(null);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { ok }
                }
            }
        };

        await dialog.ShowDialog(this);
        await tcs.Task;
    }



}

