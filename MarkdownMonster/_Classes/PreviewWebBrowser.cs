﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using FontAwesome.WPF;
using MarkdownMonster.Windows;
using Westwind.Utilities;

namespace MarkdownMonster
{
    public class PreviewWebBrowser 
    {
        /// <summary>
        /// Instance of the Web Browser control that hosts ACE Editor
        /// </summary>
        public WebBrowser WebBrowser { get; set; }

        public dynamic BrowserPreview { get; set; }


        WebBrowserHostUIHandler wbHandler;
        
        /// <summary>
        /// Reference back to the main Markdown Monster window that 
        /// </summary>
        public MainWindow Window { get; set; }

        public AppModel Model { get; set; }

        public PreviewWebBrowser(WebBrowser browser)
        {
            WebBrowser = browser;
            Model = mmApp.Model;
            Window = Model.Window;
            
            InitializePreviewBrowser();
            
            wbHandler = new WebBrowserHostUIHandler(browser);            
        }
        

        // IMPORTANT: for browser COM CSE errors which can happen with script errors
        [HandleProcessCorruptedStateExceptions]
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public void PreviewMarkdown(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false,
            bool showInBrowser = false)
        {
            try
            {
                // only render if the preview is actually visible and rendering in Preview Browser
                if (!Model.IsPreviewBrowserVisible && !showInBrowser)
                    return;

                if (editor == null)
                    editor = Window.GetActiveMarkdownEditor();

                if (editor == null)
                    return;

                var doc = editor.MarkdownDocument;
                var ext = Path.GetExtension(doc.Filename).ToLower().Replace(".", "");

                string renderedHtml = null;

                // only show preview for Markdown and HTML documents
                Model.Configuration.EditorExtensionMappings.TryGetValue(ext, out string mappedTo);
                mappedTo = mappedTo ?? string.Empty;
                if (string.IsNullOrEmpty(ext) || mappedTo == "markdown" || mappedTo == "html")
                {
                    dynamic dom = null;
                    if (!showInBrowser)
                    {
                        if (keepScrollPosition)
                        {
                            dom = WebBrowser.Document;
                            editor.MarkdownDocument.LastEditorLineNumber = dom.documentElement.scrollTop;
                        }
                        else
                        {
                            Window.ShowPreviewBrowser(false, false);
                            editor.MarkdownDocument.LastEditorLineNumber = 0;
                        }
                    }

                    if (mappedTo == "html")
                    {
                        if (!editor.MarkdownDocument.WriteFile(editor.MarkdownDocument.HtmlRenderFilename,
                                editor.MarkdownDocument.CurrentText))
                            // need a way to clear browser window
                            return;
                    }
                    else
                    {
                        bool usePragma = !showInBrowser && mmApp.Configuration.PreviewSyncMode != PreviewSyncMode.None;
                        renderedHtml = editor.MarkdownDocument.RenderHtmlToFile(usePragmaLines: usePragma,
                                        renderLinksExternal: mmApp.Configuration.MarkdownOptions.RenderLinksAsExternal);
                        if (renderedHtml == null)
                        {
                            Window.SetStatusIcon(FontAwesomeIcon.Warning, Colors.Red, false);
                            Window.ShowStatus($"Access denied: {Path.GetFileName(editor.MarkdownDocument.Filename)}", 5000);
                            // need a way to clear browser window

                            return;
                        }

                        renderedHtml = StringUtils.ExtractString(renderedHtml,
                            "<!-- Markdown Monster Content -->",
                            "<!-- End Markdown Monster Content -->");
                    }

                    if (showInBrowser)
                    {
                        var url = editor.MarkdownDocument.HtmlRenderFilename;
                        mmFileUtils.ShowExternalBrowser(url);
                        return;
                    }
                    else
                    {
                        WebBrowser.Cursor = Cursors.None;
                        WebBrowser.ForceCursor = true;

                        // if content contains <script> tags we must do a full page refresh
                        bool forceRefresh = renderedHtml != null && renderedHtml.Contains("<script ");


                        if (keepScrollPosition && !mmApp.Configuration.AlwaysUsePreviewRefresh && !forceRefresh)
                        {
                            string browserUrl = WebBrowser.Source.ToString().ToLower();
                            string documentFile = "file:///" +
                                                  editor.MarkdownDocument.HtmlRenderFilename.Replace('\\', '/')
                                                      .ToLower();
                            if (browserUrl == documentFile)
                            {
                                dom = WebBrowser.Document;
                                //var content = dom.getElementById("MainContent");


                                if (string.IsNullOrEmpty(renderedHtml))
                                    PreviewMarkdown(editor, false, false); // fully reload document
                                else
                                {
                                    try
                                    {
                                        // explicitly update the document with JavaScript code
                                        // much more efficient and non-jumpy and no wait cursor
                                        var window = dom.parentWindow;
                                        window.updateDocumentContent(renderedHtml);

                                        try
                                        {
                                            // scroll preview to selected line
                                            if (mmApp.Configuration.PreviewSyncMode == PreviewSyncMode.EditorAndPreview ||
                                                mmApp.Configuration.PreviewSyncMode == PreviewSyncMode.EditorToPreview)
                                            {
                                                int lineno = editor.GetLineNumber();
                                                if (lineno > -1)
                                                    window.scrollToPragmaLine(lineno);
                                            }
                                        }
                                        catch
                                        {
                                            /* ignore scroll error */
                                        }
                                    }
                                    catch
                                    {
                                        // Refresh doesn't fire Navigate event again so
                                        // the page is not getting initiallized properly
                                        //PreviewBrowser.Refresh(true);
                                        WebBrowser.Tag = "EDITORSCROLL";
                                        WebBrowser.Navigate(new Uri(editor.MarkdownDocument.HtmlRenderFilename));
                                    }
                                }

                                return;
                            }
                        }

                        WebBrowser.Tag = "EDITORSCROLL";
                        WebBrowser.Navigate(new Uri(editor.MarkdownDocument.HtmlRenderFilename));
                        return;
                    }
                }

                // not a markdown or HTML document to preview
                Window.ShowPreviewBrowser(true, keepScrollPosition);
            }
            catch (Exception ex)
            {
                mmApp.Log("PreviewMarkdown failed (Exception captured - continuing)", ex);
            }
        }

        private DateTime invoked = DateTime.MinValue;

        public void PreviewMarkdownAsync(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false)
        {
            if (!mmApp.Configuration.IsPreviewVisible)
                return;

            var current = DateTime.UtcNow;

            // prevent multiple stacked refreshes
            if (invoked == DateTime.MinValue) // || current.Subtract(invoked).TotalMilliseconds > 4000)
            {
                invoked = current;

                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                    {
                        try
                        {
                            PreviewMarkdown(editor, keepScrollPosition);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Preview Markdown Async Exception: " + ex.Message);
                        }
                        finally
                        {
                            invoked = DateTime.MinValue;
                        }
                    }));
            }
        }



        private void InitializePreviewBrowser()
        {
            // wbhandle has additional browser initialization code
            // using the WebBrowserHostUIHandler
            WebBrowser.LoadCompleted += PreviewBrowserOnLoadCompleted;            
        }


        private void PreviewBrowserOnLoadCompleted(object sender, NavigationEventArgs e)
        {
            string url = e.Uri.ToString();
            if (!url.Contains("_MarkdownMonster_Preview"))
                return;

            bool shouldScrollToEditor = WebBrowser.Tag != null && WebBrowser.Tag.ToString() == "EDITORSCROLL";
            WebBrowser.Tag = null;

            dynamic window = null;
            MarkdownDocumentEditor editor = null;
            try
            {
                editor = Window.GetActiveMarkdownEditor();
                dynamic dom = WebBrowser.Document;
                window = dom.parentWindow;
                dom.documentElement.scrollTop = editor.MarkdownDocument.LastEditorLineNumber;

                window.initializeinterop(editor);

                if (shouldScrollToEditor)
                {
                    try
                    {
                        // scroll preview to selected line
                        if (mmApp.Configuration.PreviewSyncMode == PreviewSyncMode.EditorAndPreview ||
                            mmApp.Configuration.PreviewSyncMode == PreviewSyncMode.EditorToPreview)
                        {
                            int lineno = editor.GetLineNumber();
                            if (lineno > -1)
                                window.scrollToPragmaLine(lineno);
                        }
                    }
                    catch
                    {
                        /* ignore scroll error */
                    }
                }
            }
            catch
            {
                // try again
                Task.Delay(500).ContinueWith(t =>
                {
                    try
                    {
                        window.initializeinterop(editor);
                    }
                    catch
                    {
                        //mmApp.Log("Preview InitializeInterop failed: " + url, ex);
                    }
                });
            }
        }        
    }
}
