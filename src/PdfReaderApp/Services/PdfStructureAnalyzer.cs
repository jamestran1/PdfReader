using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfiumViewer.Core;

namespace PdfReaderApp.Services;

public class PdfStructureAnalyzer
{
    public class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public int PageIndex { get; set; }
        public string StructureType { get; set; } = "Paragraph"; // Header, Table, etc.
    }

    public List<TextChunk> Analyze(PdfDocument document)
    {
        var chunks = new List<TextChunk>();

        for (int i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            string pageText = page.GetText();
            
            // Simple heuristic chunking for now
            // In a pro version, we'd use object bounding boxes to detect blocks
            var lines = pageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string type = "Paragraph";
                if (line.Length < 100 && (line.ToUpper() == line || char.IsDigit(line[0])))
                {
                    type = "Heading/List";
                }

                chunks.Add(new TextChunk
                {
                    Text = line,
                    PageIndex = i,
                    StructureType = type
                });
            }
        }

        return chunks;
    }
}