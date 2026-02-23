using System;
using System.Runtime.InteropServices;

namespace Wolpope.Services
{
    // ═══════════════════════════════════════════════════════════════
    //  COM-интерфейс IDesktopWallpaper
    //  Позволяет устанавливать разные обои на каждый монитор.
    //  Доступен начиная с Windows 8.
    // ═══════════════════════════════════════════════════════════════

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDesktopWallpaper
    {
        // Устанавливает обои для конкретного монитора
        void SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        // Получает текущие обои монитора
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID);

        // Получает ID монитора по индексу
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        // Количество мониторов
        uint GetMonitorDevicePathCount();

        // Получает прямоугольник монитора
        [return: MarshalAs(UnmanagedType.Struct)]
        TagRect GetMonitorRECT(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID);

        // Устанавливает цвет фона
        void SetBackgroundColor(uint color);

        // Получает цвет фона
        uint GetBackgroundColor();

        // Устанавливает позицию обоев
        void SetPosition(DesktopWallpaperPosition position);

        // Получает позицию обоев
        DesktopWallpaperPosition GetPosition();

        // Устанавливает слайд-шоу
        void SetSlideshow(IntPtr items);

        // Получает слайд-шоу
        IntPtr GetSlideshow();

        // Устанавливает тайминг слайд-шоу
        void SetSlideshowOptions(
            uint options, uint slideshowTick);

        // Получает тайминг слайд-шоу
        void GetSlideshowOptions(
            out uint options, out uint slideshowTick);

        // Переход к следующему в слайд-шоу
        void AdvanceSlideshow(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            uint direction);

        // Получает статус слайд-шоу
        uint GetStatus();

        // Включает/выключает обои
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    // Класс-оболочка COM-объекта
    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    public class DesktopWallpaperClass { }

    // Позиция обоев
    public enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    // Структура RECT для GetMonitorRECT
    [StructLayout(LayoutKind.Sequential)]
    public struct TagRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
