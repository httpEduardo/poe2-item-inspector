using System;
using System.Windows;
using PoE2Inspector.Domain;

namespace PoE2Inspector.Services.Overlay;

public interface IOverlayService
{
    void Show();
    void Hide();
    void UpdateGameRect(Rect rect);
    void ShowItemAtCursor(Item item, Point cursorPositionScreen);
    void MakeOverlayClickThrough();
}
