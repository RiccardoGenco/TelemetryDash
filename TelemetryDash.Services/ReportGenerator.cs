using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Services;

public class ReportGenerator : IReportGenerator
{
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(ILogger<ReportGenerator> logger)
    {
        _logger = logger;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> GenerateAsync(TelemetrySession session, string outputPath)
    {
        return await Task.Run(() =>
        {
            var channelGroups = session.Readings
                .GroupBy(r => r.ChannelId)
                .OrderBy(g => g.Key)
                .ToList();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                    page.Header().Element(header =>
                    {
                        header.Column(col =>
                        {
                            col.Item().Text("TelemetryDash - Session Report")
                                .FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                            col.Item().PaddingTop(5).Text($"Session ID: {session.Id}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                            col.Item().Text($"Period: {session.StartTime:yyyy-MM-dd HH:mm:ss} - {session.EndTime:yyyy-MM-dd HH:mm:ss}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                            col.Item().Text($"Data Source: {session.DataSourceName}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                            col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        });
                    });

                    page.Content().Element(content =>
                    {
                        content.PaddingVertical(10).Column(col =>
                        {
                            // Channel Statistics
                            col.Item().Text("Channel Statistics").FontSize(14).Bold();
                            col.Item().PaddingTop(5);

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1);
                                });

                                // Header
                                table.Header(h =>
                                {
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Channel").FontColor(Colors.White).Bold();
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Min").FontColor(Colors.White).Bold();
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Max").FontColor(Colors.White).Bold();
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Mean").FontColor(Colors.White).Bold();
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Std Dev").FontColor(Colors.White).Bold();
                                    h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                        .Text("Count").FontColor(Colors.White).Bold();
                                });

                                foreach (var group in channelGroups)
                                {
                                    var values = group.Select(r => r.Value).ToList();
                                    var mean = values.Average();
                                    var stdDev = Math.Sqrt(values.Average(v => Math.Pow(v - mean, 2)));

                                    var bg = channelGroups.IndexOf(group) % 2 == 0
                                        ? Colors.White : Colors.Grey.Lighten4;

                                    table.Cell().Background(bg).Padding(4).Text(group.Key);
                                    table.Cell().Background(bg).Padding(4).Text($"{values.Min():F3}");
                                    table.Cell().Background(bg).Padding(4).Text($"{values.Max():F3}");
                                    table.Cell().Background(bg).Padding(4).Text($"{mean:F3}");
                                    table.Cell().Background(bg).Padding(4).Text($"{stdDev:F3}");
                                    table.Cell().Background(bg).Padding(4).Text($"{values.Count}");
                                }
                            });

                            // Alarms table
                            if (session.Alarms.Count > 0)
                            {
                                col.Item().PaddingTop(15).Text("Alarm History").FontSize(14).Bold();
                                col.Item().PaddingTop(5);

                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.RelativeColumn(2);
                                        cols.RelativeColumn(1.5f);
                                        cols.RelativeColumn(1);
                                        cols.RelativeColumn(4);
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Background(Colors.Red.Darken2).Padding(4)
                                            .Text("Timestamp").FontColor(Colors.White).Bold();
                                        h.Cell().Background(Colors.Red.Darken2).Padding(4)
                                            .Text("Channel").FontColor(Colors.White).Bold();
                                        h.Cell().Background(Colors.Red.Darken2).Padding(4)
                                            .Text("Severity").FontColor(Colors.White).Bold();
                                        h.Cell().Background(Colors.Red.Darken2).Padding(4)
                                            .Text("Message").FontColor(Colors.White).Bold();
                                    });

                                    foreach (var alarm in session.Alarms.OrderByDescending(a => a.Timestamp).Take(50))
                                    {
                                        table.Cell().Padding(3).Text(alarm.Timestamp.ToString("HH:mm:ss.fff"));
                                        table.Cell().Padding(3).Text(alarm.Reading.ChannelId);
                                        table.Cell().Padding(3).Text(alarm.Severity.ToString());
                                        table.Cell().Padding(3).Text(alarm.Message);
                                    }
                                });
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Generated by TelemetryDash v1.0.0 on ");
                        text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        text.Span(" | Page ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf(outputPath);

            // Sign the PDF
            var signature = SignDocument(outputPath);
            var sigPath = outputPath + ".sig";
            File.WriteAllBytes(sigPath, signature);

            _logger.LogInformation("Report generated: {Path} (signature: {SigPath})", outputPath, sigPath);
            return outputPath;
        });
    }

    private static byte[] SignDocument(string filePath)
    {
        using var rsa = RSA.Create(2048);
        var pdfBytes = File.ReadAllBytes(filePath);
        return rsa.SignData(pdfBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
