#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectWindowFileSizeColumn
{
    private const string ToolWindowPath = "Tools/ProjectWindow/Details";

    // Width reserved on the right side of each Project window row.
    private const float ColumnWidth = 80f;
    private const float RightPadding = 6f;

    static ProjectWindowFileSizeColumn()
    {
        if (Application.isBatchMode)
            return;

        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;

        if (IsEnabled())
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    [MenuItem(ToolWindowPath, priority = 4)]
    public static void ToggleProjectAdditionalDetails()
    {
        try
        {
            Toggle();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem(ToolWindowPath, true)]
    private static bool ToggleFeatureValidate()
    {
        Menu.SetChecked(ToolWindowPath, IsEnabled());
        return true; // menu item remains enabled
    }

    private static bool IsEnabled()
    {
        if (Application.isBatchMode)
            return false;
        return EditorPrefs.GetBool(nameof(ProjectWindowFileSizeColumn), false);
    }

    private static void Toggle()
    {
        var newValue = !IsEnabled();

        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
        if (newValue)
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;

        EditorPrefs.SetBool(nameof(ProjectWindowFileSizeColumn), newValue);
    }

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        if (Application.isBatchMode)
            return;
        
        // Only draw for list mode rows, not large icon/grid mode.
        // In grid mode the rows are much taller than a standard line.
        if (selectionRect.height > 20f || selectionRect.width < 250f)
            return;

        if (string.IsNullOrEmpty(guid))
            return;

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(assetPath))
            return;

        // Skip folders
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            var size = GetDirectorySizeBytes(assetPath);
            string sizeDirectory = FormatFileSize(size);

            DrawInfoColumn(selectionRect, sizeDirectory);

            return;
        }


        var fullPath = GetFullPath(assetPath);
        if (string.IsNullOrEmpty(fullPath))
            return;

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch
        {
            return;
        }

        if (!fileInfo.Exists)
            return;

        string sizeText = FormatFileSize(fileInfo.Length);

        DrawInfoColumn(selectionRect, sizeText);
    }

    private struct InfoData
    {
        public DateTime LastUpdated;
        public long Size;
    }

    private static Dictionary<string, InfoData> _cachedDirectories = new();

    private static long GetDirectorySizeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is null or empty.", nameof(path));

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        long size = 0;

        try
        {
            var directoryInfo = new DirectoryInfo(path);
            if (!directoryInfo.Exists)
                return 0;
            var lastModified = directoryInfo.LastWriteTimeUtc;
            if (_cachedDirectories.TryGetValue(path, out var lastCached) && lastCached.LastUpdated.Equals(lastModified))
            {
                return lastCached.Size;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (UnauthorizedAccessException)
                {
                    // skip inaccessible files
                }
                catch (PathTooLongException)
                {
                    // skip invalid paths
                }
                catch (FileNotFoundException)
                {
                    // file may disappear during enumeration
                }
            }

            _cachedDirectories[path] = new InfoData()
            {
                LastUpdated = lastModified,
                Size = size,
            };
        }
        catch
        {
        }

        return size;
    }

    private static void DrawInfoColumn(Rect selectionRect, string sizeText)
    {
        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = EditorStyles.label.fontSize,
            normal =
            {
                textColor = GetSecondaryTextColor()
            }
        };

        Rect sizeRect = new Rect(
            selectionRect.xMax - ColumnWidth - RightPadding,
            selectionRect.y,
            ColumnWidth,
            selectionRect.height
        );

        // Optional: avoid drawing over the asset name if the row is too narrow.
        if (sizeRect.xMin <= selectionRect.x + 100f)
            return;

        GUI.Label(sizeRect, sizeText, style);
    }

    private static string? GetFullPath(string assetPath)
    {
        try
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            return Path.Combine(projectRoot, assetPath);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "KB", "MB", "GB", "TB" };

        double size = bytes / 1024.0;
        int unit = 0;

        while (size >= 1024.0 && unit < units.Length - 1)
        {
            size /= 1024.0;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static Color GetSecondaryTextColor()
    {
        // Slightly dimmed label color that works in both light/dark skin.
        Color baseColor = new Color(0.70f, 0.70f, 0.70f);

        return baseColor;
    }
}