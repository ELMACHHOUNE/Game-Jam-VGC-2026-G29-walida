using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class AudioRecordingStorage
{
    public const string DefaultFolderName = "Recordings";

    public static string GetFolderPath(string subFolder = DefaultFolderName)
    {
        string folder = string.IsNullOrWhiteSpace(subFolder) ? DefaultFolderName : subFolder.Trim();
        return Path.Combine(Application.persistentDataPath, folder);
    }

    public static string SaveWav(float[] samples, int channels, int sampleRate, string filePrefix = "recording", string subFolder = DefaultFolderName)
    {
        if (samples == null || samples.Length == 0 || channels <= 0 || sampleRate <= 0)
        {
            return null;
        }

        string dir = GetFolderPath(subFolder);
        Directory.CreateDirectory(dir);

        string safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "recording" : filePrefix.Trim();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string filePath = Path.Combine(dir, $"{safePrefix}_{timestamp}.wav");

        byte[] wavBytes = EncodeWav16(samples, channels, sampleRate);
        File.WriteAllBytes(filePath, wavBytes);
        return filePath;
    }

    public static string[] GetSavedWavPaths(string subFolder = DefaultFolderName)
    {
        string dir = GetFolderPath(subFolder);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        string[] files = Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static AudioClip LoadWav(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 44)
        {
            return null;
        }

        int channels = BitConverter.ToInt16(bytes, 22);
        int sampleRate = BitConverter.ToInt32(bytes, 24);
        int bitsPerSample = BitConverter.ToInt16(bytes, 34);
        if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
        {
            return null;
        }

        int dataOffset = FindDataChunkOffset(bytes);
        if (dataOffset < 0 || dataOffset + 8 > bytes.Length)
        {
            return null;
        }

        int dataSize = BitConverter.ToInt32(bytes, dataOffset + 4);
        int pcmStart = dataOffset + 8;
        int availableBytes = Mathf.Min(dataSize, bytes.Length - pcmStart);
        int sampleCount = availableBytes / 2;
        if (sampleCount <= 0)
        {
            return null;
        }

        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short pcm = BitConverter.ToInt16(bytes, pcmStart + i * 2);
            samples[i] = pcm / 32768f;
        }

        int framesPerChannel = sampleCount / channels;
        AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(filePath), framesPerChannel, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public static List<AudioClip> LoadAllWavs(string subFolder = DefaultFolderName)
    {
        string[] paths = GetSavedWavPaths(subFolder);
        List<AudioClip> clips = new List<AudioClip>(paths.Length);

        for (int i = 0; i < paths.Length; i++)
        {
            AudioClip clip = LoadWav(paths[i]);
            if (clip != null)
            {
                clips.Add(clip);
            }
        }

        return clips;
    }

    private static byte[] EncodeWav16(float[] samples, int channels, int sampleRate)
    {
        int dataSize = samples.Length * 2;
        using (MemoryStream ms = new MemoryStream(44 + dataSize))
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short pcm = (short)Mathf.RoundToInt(clamped * 32767f);
                writer.Write(pcm);
            }

            return ms.ToArray();
        }
    }

    private static int FindDataChunkOffset(byte[] bytes)
    {
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            if (bytes[offset] == 'd' && bytes[offset + 1] == 'a' && bytes[offset + 2] == 't' && bytes[offset + 3] == 'a')
            {
                return offset;
            }

            int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            if (chunkSize < 0)
            {
                return -1;
            }

            offset += 8 + chunkSize;
        }

        return -1;
    }
}
