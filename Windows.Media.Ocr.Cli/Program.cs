//*********************************************************
//
// Copyright (c) zh-h. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Windows.Media.Ocr.Cli
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if DEBUG
            if (args.Length == 0)
            {
                args = new string[] { "..\\..\\x.png" };
            }
#endif
            string imagePath = null;
            string language = "zh-Hans-CN";
            string outputPath = "";
            bool useClipboard = false;
            bool enableNotification = false;
            int notificationDurationMs = 5000;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "-s")
                {
                    Console.WriteLine("All of supported language");
                    foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                    {
                        Console.WriteLine(lang.LanguageTag);
                    }
                    return;
                }
                if (arg == "-h" || arg == "--help")
                {
                    PrintHelp();
                    return;
                }
                if (arg == "-c" || arg == "--clipboard")
                {
                    useClipboard = true;
                    continue;
                }
                if (arg == "-n" || arg == "--notify")
                {
                    enableNotification = true;
                    continue;
                }
                if ((arg == "-nt" || arg == "--notify-timeout") && i < args.Length - 1)
                {
                    int parsedDuration;
                    if (!int.TryParse(args[i + 1], out parsedDuration) || parsedDuration <= 0)
                    {
                        Console.WriteLine("ERROR: 通知时长必须是大于 0 的整数毫秒值。");
                        return;
                    }
                    notificationDurationMs = parsedDuration;
                    i++;
                    continue;
                }
                if (arg == "-l" && i < args.Length - 1)
                {
                    language = args[i + 1];
                    i++;
                    continue;
                }
                if (arg == "-o" || arg == "--output")
                {
                    outputPath = args[i + 1];
                    i++;
                    continue;
                }
                imagePath = arg;
            }

            if (!useClipboard && imagePath == null)
            {
                PrintHelp();
                return;
            }

            try
            {
                string result;
                bool hasRecognizedText;
                if (useClipboard)
                {
                    result = RecognizeFromClipboardAsync(language).GetAwaiter().GetResult();
                    hasRecognizedText = !string.IsNullOrWhiteSpace(result);
                    if (hasRecognizedText)
                    {
                        SetClipboardTextWithRetry(result);
                    }
                }
                else
                {
                    result = RecognizeAsync(imagePath, language).GetAwaiter().GetResult();
                    hasRecognizedText = !string.IsNullOrWhiteSpace(result);
                }

                if (outputPath != "")
                {
                    WriteTextToFile(result, outputPath);
                }
                Console.WriteLine(hasRecognizedText ? result : "未识别到文字。");

                if (enableNotification)
                {
                    ShowNotification("OCR 完成", BuildNotificationText(result, useClipboard, hasRecognizedText), ToolTipIcon.Info, notificationDurationMs);
                }
            }
            catch (Exception e)
            {
                if (enableNotification)
                {
                    ShowNotification("OCR 失败", e.Message, ToolTipIcon.Error, notificationDurationMs);
                }
                var errorMessage = e.InnerException == null ? e.Message : e.Message + " " + e.InnerException.Message;
                Console.WriteLine("ERROR: " + errorMessage);
            }
#if DEBUG
            Console.ReadLine();
#endif
        }

        static void PrintHelp()
        {
            Console.WriteLine(@"Usage: Windows.Media.Ocr.Cli.exe [options...] <image file path>
Example: Windows.Media.Ocr.Cli.exe -o c:\res.txt x.png
-l      <language>  Default:zh-Hans-CN   Specify language to reconizing
-o      <output_path> Output the resalut to a file, It should end with a file name, such as res.txt.
-c      Recognize image from clipboard and write result text back to clipboard
-n      Show a Windows notification after OCR completes
-nt     <milliseconds> Notification duration in milliseconds. Default:5000
-s      Show all supported languages
-h      Show help like this
");
        }

        static async Task<string> RecognizeFromClipboardAsync(string language)
        {
            using (var image = GetClipboardImageWithRetry())
            {
                if (image == null)
                {
                    throw new Exception("剪贴板中没有图片。");
                }

                var tempPath = Path.Combine(Path.GetTempPath(), "Windows.Media.Ocr.Cli-" + Guid.NewGuid().ToString("N") + ".png");
                image.Save(tempPath, ImageFormat.Png);

                try
                {
                    return await RecognizeAsync(tempPath, language);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            }
        }

        static Image GetClipboardImageWithRetry()
        {
            try
            {
                return RunClipboardAction(() => Clipboard.GetImage());
            }
            catch (ExternalException ex)
            {
                throw new Exception("读取剪贴板图片失败，请确认剪贴板未被其他程序占用。", ex);
            }
        }

        static void SetClipboardTextWithRetry(string text)
        {
            try
            {
                RunClipboardAction(() =>
                {
                    Clipboard.SetText(text);
                    return true;
                });
            }
            catch (ExternalException ex)
            {
                throw new Exception("写入剪贴板文本失败，请确认剪贴板未被其他程序占用。", ex);
            }
        }

        static T RunClipboardAction<T>(Func<T> action)
        {
            ExternalException lastException = null;
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    return action();
                }
                catch (ExternalException ex)
                {
                    lastException = ex;
                    Thread.Sleep(100);
                }
            }

            throw lastException ?? new ExternalException("Clipboard operation failed");
        }

        static string BuildNotificationText(string result, bool useClipboard, bool hasRecognizedText)
        {
            if (!hasRecognizedText)
            {
                return useClipboard ? "识别完成，但没有识别到文字，未更新剪贴板。" : "识别完成，但没有识别到文字。";
            }

            var preview = (result ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (preview.Length > 40)
            {
                preview = preview.Substring(0, 40) + "...";
            }

            if (preview == "")
            {
                return useClipboard ? "识别完成，结果已写回剪贴板。" : "识别完成。";
            }

            var prefix = useClipboard ? "识别完成，结果已写回剪贴板。" : "识别完成。";
            return prefix + "\n" + preview;
        }

        static void ShowNotification(string title, string text, ToolTipIcon icon, int durationMs)
        {
            var titleBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(title ?? string.Empty));
            var textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty));
            var systemIconName = icon == ToolTipIcon.Error ? "Error" : icon == ToolTipIcon.Warning ? "Warning" : "Information";
            var script = string.Format(
@"Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$title = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{0}'))
$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{1}'))
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::{2}
$notifyIcon.Visible = $true
$notifyIcon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::{3}
$notifyIcon.BalloonTipTitle = $title
$notifyIcon.BalloonTipText = $text
$notifyIcon.ShowBalloonTip({4})
Start-Sleep -Milliseconds {4}
$notifyIcon.Dispose()
",
                titleBase64,
                textBase64,
                systemIconName,
                icon,
                durationMs);
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -STA -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo);
        }

        static async Task<string> RecognizeAsync(string imagePath, string language)
        {
            StorageFile storageFile;
            var path = Path.GetFullPath(imagePath); // x.png
            var extName = Path.GetExtension(path); // .png
            var outPath = path.Replace(extName, "") + "-out" + extName;  // x-out.png
            storageFile = await StorageFile.GetFileFromPathAsync(path);
            IRandomAccessStream randomAccessStream = await storageFile.OpenReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            Globalization.Language lang = new Globalization.Language(language);
            string space = language.Contains("zh") ? "" : " ";
            string result = null;
            if (OcrEngine.IsLanguageSupported(lang))
            {
                OcrEngine engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null)
                {
                    OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);
                    foreach (var tempLine in ocrResult.Lines)
                    {
                        string line = "";
                        foreach (var word in tempLine.Words)
                        {
                            line += word.Text + space;
                        }
                        result += line + Environment.NewLine;
                    }
                }
            }
            else
            {
                throw new Exception(string.Format("Language {0} is not supported", language));
            };
            randomAccessStream.Dispose();
            softwareBitmap.Dispose();
            return await Task<string>.Run(() =>
            {
                return result ?? string.Empty;
            });
        }
        static void WriteTextToFile(string text, string filePath)
        {
            // 创建一个输出流
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                // 将字符串转换成UTF-8编码并写入文件
                using (var writer = new StreamWriter(fileStream, Encoding.UTF8))
                {
                    writer.Write(text);
                }
            }
        }
    }
}
