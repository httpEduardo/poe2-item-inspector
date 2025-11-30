using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Overlay;
using PoE2Inspector.Services.Trade;

namespace PoE2Inspector.UI;

public partial class OverlayWindow : Window
{
    private Border? _itemCard;
    private IOverlayService? _overlayService;
    private ITradeService? _tradeService;

    public OverlayWindow()
    {
        InitializeComponent();

        this.MouseMove += OverlayWindow_MouseMove;
        this.MouseLeftButtonUp += OverlayWindow_MouseLeftButtonUp;
    }

    public void SetOverlayService(IOverlayService overlayService)
    {
        _overlayService = overlayService;
    }

    public void SetTradeService(ITradeService tradeService)
    {
        _tradeService = tradeService;
    }

    public void ShowItem(Item item, Point position)
    {
        var canvas = this.Content as Canvas;
        if (canvas == null) return;

        if (_itemCard != null)
        {
            canvas.Children.Remove(_itemCard);
        }

        _itemCard = CreateItemCard(item);

        canvas.Children.Add(_itemCard);

        _itemCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardWidth = Math.Min(_itemCard.DesiredSize.Width, 350);
        var cardHeight = Math.Max(Math.Min(_itemCard.DesiredSize.Height, Height - 40), 150); 

        var x = position.X + 20;
        var y = position.Y + 20;

        if (x + cardWidth > Width)
            x = Width - cardWidth - 10;
        if (y + cardHeight > Height)
            y = Height - cardHeight - 10;

        Canvas.SetLeft(_itemCard, x);
        Canvas.SetTop(_itemCard, y);

    }

    public void HideItem()
    {
        var canvas = this.Content as Canvas;
        if (_itemCard != null && canvas != null)
        {
            canvas.Children.Remove(_itemCard);
            _itemCard = null;

            _overlayService?.MakeOverlayClickThrough();
        }
    }

    private Border CreateItemCard(Item item)
    {

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); 
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); 

        var headerBar = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 50))
        };
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); 
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); 
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); 
        headerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); 

        var copyButton = new Button
        {
            Content = "📋",
            FontSize = 14,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Copy to clipboard"
        };
        copyButton.Click += (s, e) => CopyItemToClipboard(item);
        Grid.SetColumn(copyButton, 0);
        headerBar.Children.Add(copyButton);

        var tradeButton = new Button
        {
            Content = "🔍",
            FontSize = 14,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Search on PoE2 Trade"
        };
        tradeButton.Click += (s, e) => SearchItemOnTrade(item);
        Grid.SetColumn(tradeButton, 1);
        headerBar.Children.Add(tradeButton);

        var dragHandle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll
        };
        dragHandle.MouseLeftButtonDown += (s, e) => StartDrag(e);
        Grid.SetColumn(dragHandle, 2);
        headerBar.Children.Add(dragHandle);

        var closeButton = new Button
        {
            Content = "✕",
            FontSize = 16,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Close"
        };
        closeButton.Click += (s, e) => HideItem();
        Grid.SetColumn(closeButton, 3);
        headerBar.Children.Add(closeButton);

        Grid.SetRow(headerBar, 0);
        grid.Children.Add(headerBar);

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(10)
        };

        AddTextBlock(stackPanel, item.Rarity, GetRarityColor(item.Rarity), 14, true);
        AddTextBlock(stackPanel, item.Name, Colors.White, 16, true);
        if (!string.IsNullOrEmpty(item.BaseType))
        {
            AddTextBlock(stackPanel, item.BaseType, Colors.LightGray, 14);
        }

        AddSeparator(stackPanel);

        if (item.Properties.Count > 0)
        {
            foreach (var prop in item.Properties)
            {
                var displayValue = prop.Value;

                if (prop.Label.Equals("Sockets", StringComparison.OrdinalIgnoreCase))
                {

                    var socketCount = System.Linq.Enumerable.Count(displayValue, c => !char.IsWhiteSpace(c) && c != '-');
                    displayValue = socketCount.ToString();
                }

                AddTextBlock(stackPanel, $"{prop.Label}: {displayValue}", Colors.LightBlue, 12);
            }
            AddSeparator(stackPanel);
        }

        if (item.Requirements.Count > 0)
        {
            AddTextBlock(stackPanel, "Requirements:", Colors.White, 12, true);
            foreach (var req in item.Requirements)
            {
                AddTextBlock(stackPanel, $"{req.Label}: {req.Value}", Colors.LightGray, 11);
            }
            AddSeparator(stackPanel);
        }

        if (item.ImplicitMods.Count > 0)
        {
            foreach (var mod in item.ImplicitMods)
            {
                AddTextBlock(stackPanel, mod.RawText, Colors.LightBlue, 12);
            }
            AddSeparator(stackPanel);
        }

        if (item.ExplicitMods.Count > 0)
        {
            foreach (var mod in item.ExplicitMods)
            {
                AddTextBlock(stackPanel, mod.RawText, Colors.LightSkyBlue, 12);
            }
        }

        Grid.SetRow(stackPanel, 1);
        grid.Children.Add(stackPanel);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 20, 30)),
            BorderBrush = new SolidColorBrush(GetRarityColor(item.Rarity)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Child = grid,
            MinWidth = 300,
            MaxWidth = 350,
            MinHeight = 150

        };

        return border;
    }

    private Point _dragStartPoint;
    private bool _isDragging;

    private void StartDrag(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_itemCard != null)
        {
            _isDragging = true;
            var canvas = this.Content as Canvas;
            if (canvas != null)
            {
                _dragStartPoint = e.GetPosition(canvas);
                _itemCard.CaptureMouse();
            }
        }
    }

    private void OverlayWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging && _itemCard != null)
        {
            var canvas = this.Content as Canvas;
            if (canvas != null)
            {
                var currentPoint = e.GetPosition(canvas);
                var offset = currentPoint - _dragStartPoint;

                var currentLeft = Canvas.GetLeft(_itemCard);
                var currentTop = Canvas.GetTop(_itemCard);

                if (double.IsNaN(currentLeft)) currentLeft = 0;
                if (double.IsNaN(currentTop)) currentTop = 0;

                Canvas.SetLeft(_itemCard, currentLeft + offset.X);
                Canvas.SetTop(_itemCard, currentTop + offset.Y);

                _dragStartPoint = currentPoint;
            }
        }
    }

    private void OverlayWindow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDragging && _itemCard != null)
        {
            _isDragging = false;
            _itemCard.ReleaseMouseCapture();
        }
    }

    private void CopyItemToClipboard(Item item)
    {
        try
        {
            var text = $"{item.Name}\n{item.BaseType}\n\n";

            foreach (var prop in item.Properties)
                text += $"{prop.Label}: {prop.Value}\n";

            if (item.Requirements.Count > 0)
            {
                text += "\nRequirements:\n";
                foreach (var req in item.Requirements)
                    text += $"{req.Label}: {req.Value}\n";
            }

            if (item.ImplicitMods.Count > 0)
            {
                text += "\n";
                foreach (var mod in item.ImplicitMods)
                    text += $"{mod.RawText}\n";
            }

            if (item.ExplicitMods.Count > 0)
            {
                text += "\n";
                foreach (var mod in item.ExplicitMods)
                    text += $"{mod.RawText}\n";
            }

            System.Windows.Clipboard.SetText(text);
        }
        catch { }
    }

    private void SearchItemOnTrade(Item item)
    {
        _tradeService?.OpenTradeSearch(item);
    }

    private void AddTextBlock(StackPanel parent, string text, Color color, double fontSize, bool bold = false)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    private void AddSeparator(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(100, 150, 150, 150)),
            Margin = new Thickness(0, 5, 0, 5)
        });
    }

    private Color GetRarityColor(string rarity)
    {
        return rarity.ToLower() switch
        {
            "normal" => Colors.White,
            "magic" => Color.FromRgb(136, 136, 255),
            "rare" => Color.FromRgb(255, 255, 119),
            "unique" => Color.FromRgb(175, 96, 37),
            "currency" => Colors.Gold,
            _ => Colors.Gray
        };
    }
}
