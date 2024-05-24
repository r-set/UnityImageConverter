using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;

public class ImageConverter : EditorWindow
{
    private string _inputFolderPath = "";
    private string _outputFolderPath = "";
    private int _selectedFormatIndex = 0;
    private string[] _formatsImage = new string[] { "png", "jpg" };
    private string _debugMessage = "";
    private bool _preserveTransparency = true;
    private List<string> _imagePaths;
    private int _currentImageIndex;
    private bool _isConverting;
    private bool _isCancelled;
    private bool _checkDimensionsForMultiplesOfFour = false;

    [MenuItem("Window/Image Converter")]
    public static void ShowWindow()
    {
        GetWindow<ImageConverter>("Image Converter");
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.normal.textColor = Color.yellow;

        GUILayout.Label("Image Converter", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        _inputFolderPath = EditorGUILayout.TextField("Image folder", _inputFolderPath);

        if (GUILayout.Button("Select image folder"))
        {
            _inputFolderPath = EditorUtility.OpenFolderPanel("Image folder", "", "");
        }

        _outputFolderPath = EditorGUILayout.TextField("Output folder", _outputFolderPath);

        if (GUILayout.Button("Select output folder"))
        {
            _outputFolderPath = EditorUtility.OpenFolderPanel("Output folder", "", "");
        }

        EditorGUILayout.Space();

        GUILayout.Label("Select image format");
        _selectedFormatIndex = EditorGUILayout.Popup(_selectedFormatIndex, _formatsImage);

        if (_formatsImage[_selectedFormatIndex].ToLower() == "png")
        {
            _preserveTransparency = EditorGUILayout.Toggle("Preserve Transparency", _preserveTransparency);
        }

        EditorGUILayout.Space();

        _checkDimensionsForMultiplesOfFour = EditorGUILayout.Toggle("Multiple of four", _checkDimensionsForMultiplesOfFour);

        EditorGUILayout.Space();

        GUI.enabled = !_isConverting;
        if (GUILayout.Button("Convert images"))
        {
            StartConversion();
        }
        GUI.enabled = true;

        GUI.enabled = _isConverting;
        if (GUILayout.Button("Cancel"))
        {
            _isCancelled = true;
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        GUILayout.Label(_debugMessage, style);
    }

    private void StartConversion()
    {
        if (string.IsNullOrEmpty(_inputFolderPath) || string.IsNullOrEmpty(_outputFolderPath))
        {
            _debugMessage = "Please select both input and output folders.";
            return;
        }

        _imagePaths = GetImagePaths(_inputFolderPath);
        _currentImageIndex = 0;
        _isConverting = true;
        _isCancelled = false;
        _debugMessage = "Conversion started.";

        EditorApplication.delayCall += ConvertImagesWithDelay;
    }

    private void ConvertImagesWithDelay()
    {
        if (_currentImageIndex < _imagePaths.Count && !_isCancelled)
        {
            ConvertImage(_imagePaths[_currentImageIndex]);
            _currentImageIndex++;

            EditorApplication.delayCall += ConvertImagesWithDelay;
        }
        else
        {
            _isConverting = false;
            _debugMessage = "Conversion completed.";

            EditorApplication.delayCall -= ConvertImagesWithDelay;
        }

        Repaint();
    }

    private void MakeDimensionsMultipleOfFour(ref Texture2D texture)
    {
        int newWidth = RoundToNearestMultiple(texture.width, 4);
        int newHeight = RoundToNearestMultiple(texture.height, 4);

        if (newWidth != texture.width || newHeight != texture.height)
        {
            Color[] pixels = texture.GetPixels();

            int offsetX = (newWidth - texture.width) / 2;
            int offsetY = (newHeight - texture.height) / 2;

            Color[] resizedPixels = new Color[newWidth * newHeight];

            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    resizedPixels[(y + offsetY) * newWidth + (x + offsetX)] = pixels[y * texture.width + x];
                }
            }

            Texture2D resizedTexture = new Texture2D(newWidth, newHeight, texture.format, texture.mipmapCount > 1);
            resizedTexture.SetPixels(resizedPixels);
            resizedTexture.Apply();

            Object.DestroyImmediate(texture);
            texture = resizedTexture;
        }
    }

    private int RoundToNearestMultiple(int value, int multiple)
    {
        return Mathf.CeilToInt((float)value / multiple) * multiple;
    }

    private void ConvertImage(string imagePath)
    {
        try
        {
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(imageBytes);

            if (_checkDimensionsForMultiplesOfFour)
            {
                MakeDimensionsMultipleOfFour(ref texture);
            }

            byte[] newImageBytes = null;
            string selectedFormat = _formatsImage[_selectedFormatIndex].ToLower();

            switch (selectedFormat)
            {
                case "png":
                    newImageBytes = _preserveTransparency ? texture.EncodeToPNG() : EncodeToPNGWithoutAlpha(texture);
                    break;
                case "jpg":
                    newImageBytes = EncodeToJPG(texture);
                    break;
            }

            if (newImageBytes != null)
            {
                string relativePath = imagePath.Substring(_inputFolderPath.Length + 1);
                string outputFilePath = Path.Combine(_outputFolderPath, relativePath);
                string outputDir = Path.GetDirectoryName(outputFilePath);

                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                string fileName = Path.GetFileNameWithoutExtension(imagePath) + "." + selectedFormat;
                string savePath = Path.Combine(outputDir, fileName);
                File.WriteAllBytes(savePath, newImageBytes);

                string relativeSavePath = savePath.Substring(Application.dataPath.Length - "Assets".Length);
                AssetDatabase.ImportAsset(relativeSavePath);

                TextureImporter importer = AssetImporter.GetAtPath(relativeSavePath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.alphaIsTransparency = selectedFormat == "png" && _preserveTransparency;
                    AssetDatabase.WriteImportSettingsIfDirty(relativeSavePath);
                    AssetDatabase.ImportAsset(relativeSavePath, ImportAssetOptions.ForceUpdate);
                }
            }

            Object.DestroyImmediate(texture);
        }
        catch (IOException ex)
        {
            Debug.LogError("Error reading or writing file: " + ex.Message);
        }
    }

    private List<string> GetImagePaths(string directory)
    {
        List<string> imagePaths = new List<string>();
        string[] fileEntries = Directory.GetFiles(directory);
        string[] subdirectoryEntries = Directory.GetDirectories(directory);

        foreach (string filePath in fileEntries)
        {
            if (IsImageFile(filePath))
            {
                imagePaths.Add(filePath);
            }
        }

        foreach (string subdirectory in subdirectoryEntries)
        {
            imagePaths.AddRange(GetImagePaths(subdirectory));
        }

        return imagePaths;
    }

    private bool IsImageFile(string path)
    {
        string extension = Path.GetExtension(path).ToLower();
        return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
    }

    private byte[] EncodeToJPG(Texture2D texture)
    {
        Texture2D textureWithoutAlpha = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        textureWithoutAlpha.SetPixels(texture.GetPixels());
        textureWithoutAlpha.Apply();

        return textureWithoutAlpha.EncodeToJPG();
    }

    private byte[] EncodeToPNGWithoutAlpha(Texture2D texture)
    {
        Texture2D textureWithoutAlpha = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        Color32[] pixels = texture.GetPixels32();

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i].a = 255;
        }

        textureWithoutAlpha.SetPixels32(pixels);
        textureWithoutAlpha.Apply();

        return textureWithoutAlpha.EncodeToPNG();
    }
}
