﻿using Benny_Scraper.BusinessLogic.Config;
using Benny_Scraper.Models;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Options;
using NLog;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Benny_Scraper.BusinessLogic
{
    /// <summary>
    /// Generates an epub file from a novel and its chapters. Using Epub Version 3.2 https://en.wikipedia.org/wiki/EPUB#Open_Container_Format_3.2
    /// </summary>
    public class EpubGenerator : IEpubGenerator
    {
        private readonly EpubTemplates _epubTemplates;
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        public EpubGenerator(IOptions<EpubTemplates> epubTemplates)
        {
            _epubTemplates = epubTemplates.Value;
        }

        public void CreateEpub(Novel novel, IEnumerable<Chapter> chapters, string outputFilePath)
        {
            Logger.Info("Creating epub file. Novel: {0}, Chapters: {1}, OutputFilePath: {2}", novel.Title, chapters.Count(), outputFilePath);
            string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Logger.Info("Temp directory: {0}", tempDirectory);
            Directory.CreateDirectory(tempDirectory);
            Logger.Info("Temp directory created");

            try
            {
                string mimetypeFilePath = Path.Combine(tempDirectory, "mimetype");
                File.WriteAllText(mimetypeFilePath, "application/epub+zip");

                // used templates from https://github.com/IDPF/epub3-samples
                string metaInfDirectory = Path.Combine(tempDirectory, "META-INF");
                string oebpsDirectory = Path.Combine(tempDirectory, "OEBPS");
                string textDirectory = Path.Combine(oebpsDirectory, "Text");
                string cssDirectory = Path.Combine(oebpsDirectory, "css");
                Logger.Info("Creating directories: {0}, {1}, {2}, {3}", metaInfDirectory, oebpsDirectory, textDirectory, cssDirectory);
                Directory.CreateDirectory(metaInfDirectory);
                Directory.CreateDirectory(oebpsDirectory);
                Directory.CreateDirectory(textDirectory);
                Directory.CreateDirectory(cssDirectory);
                Logger.Info("Directories created");

                XmlDocument containerXml = new XmlDocument();
                containerXml.LoadXml(_epubTemplates.ContainerXml);
                Logger.Info("Saving container.xml");
                containerXml.Save(Path.Combine(metaInfDirectory, "container.xml"));
                Logger.Info("container.xml saved");

                string manifestItems = string.Empty;
                string spineItems = string.Empty;

                int chapterIndex = 1;
                Logger.Info("Creating chapters and adding to manifest and spine");
                foreach (var chapter in chapters)
                {
                    // create variable to save chapter name replace invalid characters with _ and remove spaces
                    // use regex to replace invalid characters

                    string safeChapterTitleName = Regex.Replace(chapter.Title, "[^a-zA-Z0-9_.]+", "_", RegexOptions.Compiled);
                    string chapterFileName = $"000{chapterIndex}_{safeChapterTitleName}.xhtml";
                    string chapterFilePath = Path.Combine(textDirectory, chapterFileName);

                    string chapterContent = BuildXhtmlContent(chapter.Title, chapter.Content);
                    File.WriteAllText(chapterFilePath, chapterContent);

                    manifestItems += $"<item id=\"chapter{chapterIndex}\" href=\"Text/{chapterFileName}\" media-type=\"application/xhtml+xml\"/>";
                    spineItems += $"<itemref idref=\"chapter{chapterIndex}\"/>";

                    chapterIndex++;
                }
                Logger.Info("Chapters created and added to manifest and spine");
                manifestItems += "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>";
                manifestItems += "<item id=\"css_chapter\" href=\"css/chapter.css\" media-type=\"text/css\"/>";
                manifestItems += "<item id=\"css_nav\" href=\"css/nav.css\" media-type=\"text/css\"/>";
                manifestItems += "<item id=\"css_toc\" href=\"css/toc.css\" media-type=\"text/css\"/>";

                string updatedContentOpf = string.Format(_epubTemplates.ContentOpf, novel.Title, novel.Title, novel.Author, manifestItems, spineItems);

                XmlDocument contentOpf = new XmlDocument();
                contentOpf.LoadXml(updatedContentOpf);
                Logger.Info("Saving content.opf");
                contentOpf.Save(Path.Combine(oebpsDirectory, "content.opf"));
                Logger.Info("content.opf saved");

                // Create nav.xhtml
                XmlDocument navXhtml = new XmlDocument();
                navXhtml.LoadXml(_epubTemplates.NavXhtml);
                var navList = navXhtml.SelectSingleNode("//*[local-name()='ol']");
                chapterIndex = 1;
                foreach (var chapter in chapters)
                {
                    string safeChapterTitleName = Regex.Replace(chapter.Title, "[^a-zA-Z0-9_.]+", "_", RegexOptions.Compiled);
                    string chapterFileName = $"000{chapterIndex}_{safeChapterTitleName}.xhtml";

                    XmlElement navItem = navXhtml.CreateElement("li");
                    XmlElement navLink = navXhtml.CreateElement("a");
                    navLink.SetAttribute("href", $"Text/{chapterFileName}");
                    navLink.InnerText = chapter.Title;
                    navItem.AppendChild(navLink);
                    navList.AppendChild(navItem);

                    chapterIndex++;
                }

                navXhtml.Save(Path.Combine(oebpsDirectory, "nav.xhtml"));
                Logger.Info("nav.xhtml saved");

                // Add CSS files
                File.WriteAllText(Path.Combine(cssDirectory, "chapter.css"), _epubTemplates.ChapterCss);
                File.WriteAllText(Path.Combine(cssDirectory, "nav.css"), _epubTemplates.NavCss);
                File.WriteAllText(Path.Combine(cssDirectory, "toc.css"), _epubTemplates.TocCss);

                Logger.Info("Compressing everything into an epub file");

                // Compress everything into an epub file
                using (FileStream fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    using (ZipOutputStream zipStream = new ZipOutputStream(fs))
                    {
                        // Add mimetype file
                        ZipEntry mimetypeEntry = new ZipEntry("mimetype");
                        mimetypeEntry.CompressionMethod = CompressionMethod.Stored; // No compression for mimetype file
                        zipStream.PutNextEntry(mimetypeEntry);
                        byte[] mimetypeBuffer = File.ReadAllBytes(mimetypeFilePath);
                        zipStream.Write(mimetypeBuffer, 0, mimetypeBuffer.Length);
                        zipStream.CloseEntry();

                        // Add META-INF and OEBPS files
                        AddDirectoryToZip(zipStream, metaInfDirectory, "META-INF", tempDirectory);
                        AddDirectoryToZip(zipStream, oebpsDirectory, "OEBPS", tempDirectory);
                    }
                }
                Logger.Info("Epub file created at: {0}", outputFilePath);
            }
            catch (Exception ex)
            {
                Logger.Fatal($"Error when generating Epub for Novel: {novel.Title} Novel Id: {novel.Id}. {ex}");
            }
            finally
            {
                Logger.Info($"Deleting temporary directory: {tempDirectory}");
                // Delete temporary directory
                Directory.Delete(tempDirectory, true);
                Logger.Info($"Deleted temporary directory: {tempDirectory}");
            }
        }



        private void AddDirectoryToZip(ZipOutputStream zip, string directoryPath, string entryPath)
        {
            AddDirectoryToZip(zip, directoryPath, entryPath);
        }

        private void AddDirectoryToZip(ZipOutputStream zipStream, string sourceDirectory, string targetDirectory, string baseDirectory)
        {
            DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);

            foreach (FileInfo fileInfo in diSource.GetFiles())
            {
                string entryName = Path.Combine(targetDirectory, fileInfo.Name).Replace("\\", "/");
                ZipEntry entry = new ZipEntry(entryName);
                entry.CompressionMethod = CompressionMethod.Deflated;
                zipStream.PutNextEntry(entry);
                byte[] buffer = File.ReadAllBytes(fileInfo.FullName);
                zipStream.Write(buffer, 0, buffer.Length);
                zipStream.CloseEntry();
            }

            foreach (DirectoryInfo sourceSubDir in diSource.GetDirectories())
            {
                string nextTargetSubDir = Path.Combine(targetDirectory, sourceSubDir.Name);
                AddDirectoryToZip(zipStream, sourceSubDir.FullName, nextTargetSubDir, baseDirectory);
            }
        }

        private string BuildXhtmlContent(string title, string content)
        {
            StringBuilder xhtmlContentBuilder = new StringBuilder();

            xhtmlContentBuilder.AppendLine("<div>");
            xhtmlContentBuilder.AppendFormat("<h2>{0}</h2>", title);

            string[] paragraphs = content.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string paragraph in paragraphs)
            {
                xhtmlContentBuilder.AppendFormat("<p>{0}</p>", paragraph.Trim());
            }

            xhtmlContentBuilder.AppendLine("</div>");

            return string.Format(_epubTemplates.ChapterContent, title, xhtmlContentBuilder.ToString());
        }



        //create a method to open an epub and validate it, it should be very robust on the problems
        public void ValidateEpub(string epubFilePath)
        {
            //check if the file exists
            if (!File.Exists(epubFilePath))
            {
                throw new FileNotFoundException($"Epub file not found: {epubFilePath}");
            }

            //check if the file is a valid epub
            using (FileStream fs = new FileStream(epubFilePath, FileMode.Open, FileAccess.Read))
            {
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    //check if the mimetype file exists
                    var mimetypeEntry = zip.Entries.FirstOrDefault(x => x.FullName == "mimetype");
                    if (mimetypeEntry == null)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is missing.");
                    }

                    //check if the mimetype file is the first file in the zip
                    if (zip.Entries.IndexOf(mimetypeEntry) != 0)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is not the first file in the zip.");
                    }

                    //check if the mimetype file is uncompressed, do not use mimetypeEntry.CompressionLevel as it is not supported in .net core
                    if (mimetypeEntry.CompressedLength != mimetypeEntry.Length)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is compressed.");
                    }

                    //check if the mimetype file is not empty
                    if (mimetypeEntry.Length == 0)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is empty.");
                    }

                    //check if the mimetype file is not empty
                    if (mimetypeEntry.Length > 100)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is too big.");
                    }

                    //check if the mimetype file is not empty
                    if (mimetypeEntry.Length < 20)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is too small.");
                    }

                    //check if the mimetype file is not empty
                    if (mimetypeEntry.Length != 20)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is not 20 bytes.");
                    }

                    //check if the mimetype file is not empty
                    if (mimetypeEntry.Length != 20)
                    {
                        throw new Exception("Epub file is not valid. Mimetype file is not 20 bytes.");
                    }
                }
            }
        }
    }
}

