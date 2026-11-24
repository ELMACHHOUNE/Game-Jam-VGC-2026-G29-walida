using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class PhotoStorage
{
    public const string DefaultFolderName = "Photos";

    public static string GetFolderPath(string subFolder = DefaultFolderName)
    {
        string folder = string.IsNullOrWhiteSpace(subFolder) ? DefaultFolderName : subFolder.Trim();
        return Path.Combine(Application.persistentDataPath, folder);
    }

    public static string SavePhoto(Texture2D photo, string filePrefix = "photo", string subFolder = DefaultFolderName)
    {
        if (photo == null)
        {
            return null;
        }

        string dir = GetFolderPath(subFolder);
        Directory.CreateDirectory(dir);

        string safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "photo" : filePrefix.Trim();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string filePath = Path.Combine(dir, $"{safePrefix}_{timestamp}.png");

        byte[] pngBytes = photo.EncodeToPNG();
        File.WriteAllBytes(filePath, pngBytes);
        return filePath;
    }

    public static string[] GetSavedPhotoPaths(string subFolder = DefaultFolderName)
    {
        string dir = GetFolderPath(subFolder);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        string[] files = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static Texture2D LoadPhoto(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(tex);
            return null;
        }

        return tex;
    }

    public static List<Texture2D> LoadAllPhotos(string subFolder = DefaultFolderName)
    {
        string[] paths = GetSavedPhotoPaths(subFolder);
        List<Texture2D> photos = new List<Texture2D>(paths.Length);

        for (int i = 0; i < paths.Length; i++)
        {
            Texture2D tex = LoadPhoto(paths[i]);
            if (tex != null)
            {
                photos.Add(tex);
            }
        }

        return photos;
    }
}
