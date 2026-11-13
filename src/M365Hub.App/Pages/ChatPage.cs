// Chat page: the app opens here. Account picker (pinned account or
// round-robin), model picker, chat / image-gen mode, uploads
// (image / file / camera), Markdown + TeX rendering via MessageView,
// history persisted by Core ChatStore. Mirrors the Android native
// MainActivity chat view, including plain-text requests (no extra
// prompts) and account_id pinning.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using M365Hub.Core;
using System.Text.Json.Nodes;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace M365Hub.App.Pages;

public sealed partial class ChatPage : UserControl
{
    private readonly AppState _state;
    private readonly MainWindow _win;
    private readonly StackPanel _msgs = new() { Spacing = 14 };
    private readonly ScrollViewer _scroll = new();
    private readonly TextBox _input = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Message Copilot",
        MinHeight = 44,
        MaxHeight = 160,
    };
    private readonly ComboBox _accountBox = new() { MinWidth = 110 };
    private readonly ComboBox _modelBox = new() { MinWidth = 200 };
    private readonly ComboBox _modeBox = new() { MinWidth = 100 };
    private readonly ComboBox _imageModelBox = new() { MinWidth = 140, Visibility = Visibility.Collapsed };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly List<Att> _atts = new();
    private string _model = "";
    private string _imageModel = "flux-4";
    private bool _sending;

    private sealed class Att
    {
        public string FilePath = "";
        public string Name = "";
        public bool IsImage;
    }

    public ChatPage(AppState state, MainWindow win)
    {
        _state = state;
        _win = win;
        _modeBox.Items.Add("聊天");
        _modeBox.Items.Add("生成图片");
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectionChanged += (_, _) =>
            _imageModelBox.Visibility = _modeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

        _accountBox.SelectionChanged += (_, _) =>
        {
            if (_accountBox.SelectedItem is ComboBoxItem item && item.Tag is string id)
                _state.ChatAccountId = id;
        };
        _modelBox.SelectionChanged += (_, _) =>
        {
            if (_modelBox.SelectedItem is string m) { _model = m; _state.ChatModel = m; }
        };
        _imageModelBox.SelectionChanged += (_, _) =>
        {
            if (_imageModelBox.SelectedItem is string m) _imageModel = m;
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter
                && !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                    Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                e.Handled = true;
                _ = SendAsync();
            }
        };

        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12, 16, 0) };
        topBar.Children.Add(_accountBox);
        topBar.Children.Add(_modelBox);
        topBar.Children.Add(_modeBox);
        topBar.Children.Add(_imageModelBox);
        var newBtn = new GlassButton("＋ 新对话", (_, _) =>
        {
            _state.Chat.NewConv();
            _win.RefreshHistory();
            RenderMessages();
        });
        topBar.Children.Add(newBtn);

        _scroll.Content = _msgs;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var msgsWrap = new Grid { Margin = new Thickness(16, 12, 16, 0) };
        msgsWrap.Children.Add(_scroll);

        var inputBar = new Grid { Margin = new Thickness(16, 8, 16, 16) };
        inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var plus = new GlassButton();
        plus.Content = "＋";
        plus.Click += (_, _) => ShowAttachMenu(plus);
        Grid.SetColumn(plus, 0);
        Grid.SetColumn(_input, 1);
        _input.Margin = new Thickness(8, 0, 8, 0);
        var send = new GlassButton("↑", async (_, _) => await SendAsync());
        Grid.SetColumn(send, 2);
        inputBar.Children.Add(plus);
        inputBar.Children.Add(_input);
        inputBar.Children.Add(send);

        var chipsWrap = new ScrollViewer
        {
            Content = _chips,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 4, 16, 0),
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topBar, 0);
        Grid.SetRow(msgsWrap, 1);
        Grid.SetRow(chipsWrap, 2);
        Grid.SetRow(inputBar, 3);
        root.Children.Add(topBar);
        root.Children.Add(msgsWrap);
        root.Children.Add(chipsWrap);
        root.Children.Add(inputBar);
        Content = root;

        _ = LoadPickersAsync();
        RenderMessages();
    }

    private uint TextColor => _state.ThemeDark ? 0xFFFFFFFFu : 0xFF1A1A1Au;

    private async Task LoadPickersAsync()
    {
        try
        {
            var d = await _state.Api.GetAsync("/api/accounts");
            var accounts = new List<AccountInfo>();
            foreach (var item in ApiClient.Arr(d, "accounts"))
                accounts.Add(AccountInfo.FromJson(item!.AsObject()));
            _accountBox.Items.Clear();
            _accountBox.Items.Add(new ComboBoxItem { Content = "轮询", Tag = "" });
            foreach (var a in accounts)
                _accountBox.Items.Add(new ComboBoxItem { Content = a.ShortLabel, Tag = a.Id });
            string pinned = _state.ChatAccountId;
            int sel = 0;
            for (int i = 0; i < _accountBox.Items.Count; i++)
            {
                if (_accountBox.Items[i] is ComboBoxItem ci && (ci.Tag as string) == pinned) { sel = i; break; }
            }
            _accountBox.SelectedIndex = sel;
        }
        catch { _accountBox.Items.Add(new ComboBoxItem { Content = "轮询", Tag = "" }); _accountBox.SelectedIndex = 0; }

        try
        {
            var d = await _state.Api.GetAsync("/api/admin/models");
            var models = ModelInfo.FromJson(d["models"]);
            var imageModels = models.Where(m =>
                m.Id.Contains("flux", StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains("dall", StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains("image", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Id).ToList();
            if (imageModels.Count == 0) imageModels = new List<string> { "flux-4", "flux-3" };
            _modelBox.Items.Clear();
            foreach (var m in models) _modelBox.Items.Add(m.Id);
            string want = _state.ChatModel;
            if (want.Length == 0)
                // Default is gpt-5.6-reasoning (6.x is blocked by a Premium
                // license check on basic Copilot); a previously saved model
                // choice still wins and must be switched once by hand.
                want = models.Any(m => m.Id == "gpt-5.6-reasoning") ? "gpt-5.6-reasoning"
                    : models.Any(m => m.Id == "gpt-5.6-sol") ? "gpt-5.6-sol"
                    : models.Any(m => m.Id == "gpt-4-auto") ? "gpt-4-auto"
                    : models.Count > 0 ? models[0].Id : "";
            _model = want;
            _modelBox.SelectedItem = want;
            _imageModelBox.Items.Clear();
            foreach (var m in imageModels) _imageModelBox.Items.Add(m);
            _imageModel = imageModels[0];
            _imageModelBox.SelectedItem = _imageModel;
        }
        catch { }
    }

    private void RenderMessages()
    {
        _msgs.Children.Clear();
        var conv = _state.Chat.Current();
        double maxW = Math.Max(320, _win.Bounds.Width - 360);
        if (conv.Messages.Count == 0)
        {
            _msgs.Children.Add(new TextBlock
            {
                Text = "你好，今天想聊点什么？",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 8),
            });
            string[] sugg =
            {
                "解方程 x^2 - 5x + 6 = 0",
                "写一首关于秋天的诗",
                "用 Python 写一个快速排序",
                "帮我润色一段自我介绍",
            };
            foreach (string s in sugg)
            {
                var b = new GlassButton(s, (_, _) => { _input.Text = s; _ = SendAsync(); });
                b.HorizontalAlignment = HorizontalAlignment.Center;
                _msgs.Children.Add(b);
            }
            return;
        }
        foreach (var m in conv.Messages)
        {
            var content = MessageView.Build(m, _state, TextColor, Math.Min(maxW, 680));
            if (m.Role == "user")
            {
                var bubble = Glass.Card(content, _state.ThemeDark ? ElementTheme.Dark : ElementTheme.Light, 16);
                bubble.HorizontalAlignment = HorizontalAlignment.Right;
                bubble.MaxWidth = 560;
                _msgs.Children.Add(bubble);
            }
            else
            {
                var wrap = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
                wrap.Children.Add(content);
                _msgs.Children.Add(wrap);
            }
        }
        _scroll.UpdateLayout();
        _scroll.ChangeView(null, _scroll.ScrollableHeight, null);
    }

    private void ShowAttachMenu(Button anchor)
    {
        var flyout = new MenuFlyout();
        var img = new MenuFlyoutItem { Text = "上传图片" };
        img.Click += async (_, _) => await PickImageAsync();
        var file = new MenuFlyoutItem { Text = "上传文件" };
        file.Click += async (_, _) => await PickFileAsync();
        var cam = new MenuFlyoutItem { Text = "拍照上传" };
        cam.Click += async (_, _) => await CapturePhotoAsync();
        flyout.Items.Add(img);
        flyout.Items.Add(file);
        flyout.Items.Add(cam);
        flyout.ShowAt(anchor);
    }

    private async Task PickImageAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_win));
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".gif");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        await AddImageFileAsync(file);
    }

    private async Task PickFileAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_win));
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        string ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
        {
            await AddImageFileAsync(file);
            return;
        }
        var props = await file.GetBasicPropertiesAsync();
        if (props.Size > 512 * 1024)
        {
            ShowNote("文件超过 512KB，只支持 ≤512KB 的文本文件内联。");
            return;
        }
        try
        {
            string text = await FileIO.ReadTextAsync(file);
            _input.Text += (string.IsNullOrWhiteSpace(_input.Text) ? "" : "\n")
                + "```" + file.Name + "\n" + text + "\n```";
        }
        catch { ShowNote("读文件失败。"); }
    }

    private async Task CapturePhotoAsync()
    {
        try
        {
            var cam = new CameraCaptureUI();
            cam.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;
            var file = await cam.CaptureFileAsync(CameraCaptureUIMode.Photo);
            if (file != null) await AddImageFileAsync(file);
        }
        catch
        {
            ShowNote("拍照在当前环境不可用，请用上传图片。");
        }
    }

    /// <summary>Downscale to max 1568px / JPEG q85 and persist under chat-images (mirrors Android 1.2.7).</summary>
    private async Task AddImageFileAsync(StorageFile file)
    {
        try
        {
            Directory.CreateDirectory(_state.ChatImagesDir);
            string dest = Path.Combine(_state.ChatImagesDir,
                "img_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + _atts.Count + ".jpg");
            using (var src = await file.OpenReadAsync())
            {
                var dec = await BitmapDecoder.CreateAsync(src);
                var transform = new BitmapTransform();
                double scale = 1;
                if (dec.PixelWidth > 1568 || dec.PixelHeight > 1568)
                    scale = 1568.0 / Math.Max(dec.PixelWidth, dec.PixelHeight);
                transform.ScaledWidth = (uint)(dec.PixelWidth * scale);
                transform.ScaledHeight = (uint)(dec.PixelHeight * scale);
                var pixels = await dec.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                var destFile = await CreateFileAsync(dest);
                using var outStream = await destFile.OpenAsync(FileAccessMode.ReadWrite);
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    transform.ScaledWidth, transform.ScaledHeight, 96, 96, pixels.DetachPixelData());
                await enc.FlushAsync();
            }
            _atts.Add(new Att { FilePath = dest, Name = file.Name, IsImage = true });
            RefreshChips();
        }
        catch (Exception e)
        {
            ShowNote("处理图片失败：" + e.Message);
        }
    }

    private static async Task<StorageFile> CreateFileAsync(string path)
    {
        using (File.Create(path)) { }
        return await StorageFile.GetFileFromPathAsync(path);
    }

    private void RefreshChips()
    {
        _chips.Children.Clear();
        for (int i = 0; i < _atts.Count; i++)
        {
            var a = _atts[i];
            var chip = new GlassButton(a.Name + " ×", (_, _) =>
            {
                _atts.Remove(a);
                RefreshChips();
            });
            Glass.ApplyButton(chip, 20);
            _chips.Children.Add(chip);
        }
    }

    private void ShowNote(string text)
    {
        _msgs.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 60, 60)) });
    }

    private async Task SendAsync()
    {
        if (_sending) return;
        string text = _input.Text.Trim();
        if (text.Length == 0 && _atts.Count == 0) return;
        _sending = true;
        try
        {
            var conv = _state.Chat.Current();
            if (_modeBox.SelectedIndex == 1)
            {
                // Image generation mode.
                var userMsg = new ChatStore.Msg("user", text);
                conv.Messages.Add(userMsg);
                var pending = new ChatStore.Msg("assistant", "正在生成图片…");
                conv.Messages.Add(pending);
                _state.Chat.Save();
                _input.Text = "";
                RenderMessages();
                try
                {
                    var body = new JsonObject
                    {
                        ["model"] = _imageModel,
                        ["prompt"] = text,
                    };
                    if (_state.ChatAccountId.Length > 0) body["account_id"] = _state.ChatAccountId;
                    var d = await _state.Api.PostAsync("/api/chat/image", body);
                    var imgs = ApiClient.Arr(d, "images");
                    pending.Content = d["text"]?.GetValue<string>() ?? "";
                    foreach (var im in imgs) pending.Images.Add(im!.GetValue<string>());
                    if (pending.Images.Count == 0 && pending.Content.Length == 0)
                        pending.Content = "没有返回图片。";
                }
                catch (Exception e)
                {
                    pending.Content = "生成失败：" + e.Message;
                }
                _state.Chat.Save();
                _win.RefreshHistory();
                RenderMessages();
                return;
            }

            var um = new ChatStore.Msg("user", text);
            foreach (var a in _atts) um.AttImages.Add(a.FilePath);
            conv.Messages.Add(um);
            var pm = new ChatStore.Msg("assistant", "");
            conv.Messages.Add(pm);
            _state.Chat.Save();
            _input.Text = "";
            _atts.Clear();
            RefreshChips();
            RenderMessages();

            var msgsArr = new JsonArray();
            foreach (var m in conv.Messages)
            {
                if (m == pm) continue;
                msgsArr.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
            }
            var attsArr = new JsonArray();
            foreach (string path in um.AttImages)
            {
                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path);
                    attsArr.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["url"] = "data:image/jpeg;base64," + Convert.ToBase64String(bytes),
                        ["name"] = Path.GetFileName(path),
                        ["mimeType"] = "image/jpeg",
                    });
                }
                catch { }
            }
            var reqBody = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = msgsArr,
                ["attachments"] = attsArr,
            };
            if (_state.ChatAccountId.Length > 0) reqBody["account_id"] = _state.ChatAccountId;
            try
            {
                var d = await _state.Api.PostAsync("/api/chat", reqBody);
                pm.Content = d["content"]?.GetValue<string>() ?? "";
                if (pm.Content.Length == 0) pm.Content = "(空回复)";
            }
            catch (Exception e)
            {
                pm.Content = "出错：" + e.Message;
            }
            if (conv.Title == "新对话" && text.Length > 0)
                conv.Title = text.Length > 20 ? text.Substring(0, 20) : text;
            _state.Chat.Save();
            _win.RefreshHistory();
            RenderMessages();
        }
        finally
        {
            _sending = false;
        }
    }
}
