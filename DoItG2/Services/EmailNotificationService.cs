using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DoItG2.Data;
using DoItG2.Models.Common;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DoItG2.Services;

public interface IEmailNotificationService
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    Task NotifyDocumentStatusChangeAsync(string documentType, string car, string status, string notes, string? targetUserEmail = null);
    Task<int> CreateInAppNotificationAsync(string? userName, string title, string message, string type = "INFO", string? linkUrl = null);
    Task<IEnumerable<NotificationModel>> GetUserNotificationsAsync(string? userName, int limit = 10);
    Task<int> GetUnreadCountAsync(string? userName);
    Task<bool> MarkAsReadAsync(int notificationId);
    Task<bool> MarkAllAsReadAsync(string? userName);
}

public class EmailNotificationService : IEmailNotificationService
{
    private readonly DatabaseContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(DatabaseContext db, IConfiguration config, ILogger<EmailNotificationService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;

        try
        {
            var host = _config["EmailSettings:Host"] ?? "smtp.example.com";
            var port = int.TryParse(_config["EmailSettings:Port"], out var p) ? p : 587;
            var senderEmail = _config["EmailSettings:SenderEmail"] ?? "doit-g2@suzuki.co.id";
            var senderName = _config["EmailSettings:SenderName"] ?? "Do-IT G2 Suzuki Customs";
            var password = _config["EmailSettings:Password"] ?? "";
            var isConfigured = !string.IsNullOrEmpty(_config["EmailSettings:Host"]);

            if (!isConfigured)
            {
                _logger.LogInformation("[SIMULATED EMAIL] To: {To}, Subject: {Subject}", toEmail, subject);
                return;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(senderEmail, password);
            }
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email successfully sent to {To} with subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email to {To}. Continuing gracefully.", toEmail);
        }
    }

    public async Task NotifyDocumentStatusChangeAsync(string documentType, string car, string status, string notes, string? targetUserEmail = null)
    {
        var title = $"[Do-IT G2] Status Dokumen {documentType} ({car}): {status}";
        var body = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;'>
                <div style='background: #1e3a8a; padding: 20px; color: #ffffff;'>
                    <h2 style='margin: 0; font-size: 20px;'>Do-IT G2 — Sistem Kepabeanan Suzuki</h2>
                    <p style='margin: 5px 0 0 0; opacity: 0.8;'>Pemberitahuan Otomatis Perubahan Status Dokumen</p>
                </div>
                <div style='padding: 24px; color: #334155; line-height: 1.6;'>
                    <p>Halo Tim Kepabeanan,</p>
                    <p>Dokumen <strong>{documentType}</strong> dengan nomor pengajuan <strong>{car}</strong> telah diperbarui:</p>
                    <table style='width: 100%; border-collapse: collapse; margin: 16px 0;'>
                        <tr><td style='padding: 8px; border-bottom: 1px solid #e2e8f0; font-weight: bold;'>Nomor CAR / AJU</td><td style='padding: 8px; border-bottom: 1px solid #e2e8f0;'>{car}</td></tr>
                        <tr><td style='padding: 8px; border-bottom: 1px solid #e2e8f0; font-weight: bold;'>Jenis Dokumen</td><td style='padding: 8px; border-bottom: 1px solid #e2e8f0;'>{documentType}</td></tr>
                        <tr><td style='padding: 8px; border-bottom: 1px solid #e2e8f0; font-weight: bold;'>Status Terbaru</td><td style='padding: 8px; border-bottom: 1px solid #e2e8f0; color: #1e3a8a; font-weight: bold;'>{status}</td></tr>
                        <tr><td style='padding: 8px; border-bottom: 1px solid #e2e8f0; font-weight: bold;'>Catatan</td><td style='padding: 8px; border-bottom: 1px solid #e2e8f0;'>{notes ?? "-"}</td></tr>
                        <tr><td style='padding: 8px; font-weight: bold;'>Waktu Pembaruan</td><td style='padding: 8px;'>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</td></tr>
                    </table>
                    <p>Silakan masuk ke portal Do-IT G2 untuk melihat rincian dokumen.</p>
                </div>
                <div style='background: #f8fafc; padding: 12px 24px; font-size: 12px; color: #64748b; text-align: center;'>
                    © 2026 PT. Suzuki Indomobil Motor — Do-IT G2 Customs Automation Platform
                </div>
            </div>";

        // In-App Notification
        var notifType = status.Contains("SPPB") || status.Contains("NPE") || status.Contains("APPROVED") ? "SUCCESS" :
                        status.Contains("REJECT") || status.Contains("ERROR") ? "DANGER" : "INFO";

        var linkUrl = documentType.Equals("PEB", StringComparison.OrdinalIgnoreCase) ? $"/Peb/Detail?car={car}" : $"/Pib/Detail?car={car}";
        
        await CreateInAppNotificationAsync(null, title, $"Status dokumen {car} berubah menjadi {status}. {notes}", notifType, linkUrl);

        // Email Alert
        if (!string.IsNullOrEmpty(targetUserEmail))
        {
            await SendEmailAsync(targetUserEmail, title, body);
        }
    }

    public async Task<int> CreateInAppNotificationAsync(string? userName, string title, string message, string type = "INFO", string? linkUrl = null)
    {
        try
        {
            var sql = @"INSERT INTO DOIT_NOTIFIKASI (USER_NAME, TITLE, MESSAGE, TYPE, LINK_URL, IS_READ, CREATED_AT)
                        VALUES (@UserName, @Title, @Message, @Type, @LinkUrl, 0, GETDATE())";
            return await _db.ExecuteAsync(sql, new { UserName = userName, Title = title, Message = message, Type = type, LinkUrl = linkUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating in-app notification");
            return 0;
        }
    }

    public async Task<IEnumerable<NotificationModel>> GetUserNotificationsAsync(string? userName, int limit = 10)
    {
        try
        {
            var sql = @"SELECT TOP (@Limit) ID, USER_NAME AS UserName, TITLE AS Title, MESSAGE AS Message, 
                               TYPE AS Type, LINK_URL AS LinkUrl, IS_READ AS IsRead, CREATED_AT AS CreatedAt
                        FROM DOIT_NOTIFIKASI
                        WHERE (USER_NAME = @UserName OR USER_NAME IS NULL OR USER_NAME = '')
                        ORDER BY CREATED_AT DESC";
            return await _db.QueryAsync<NotificationModel>(sql, new { UserName = userName, Limit = limit });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notifications");
            return new List<NotificationModel>();
        }
    }

    public async Task<int> GetUnreadCountAsync(string? userName)
    {
        try
        {
            var sql = @"SELECT COUNT(*) FROM DOIT_NOTIFIKASI 
                        WHERE IS_READ = 0 AND (USER_NAME = @UserName OR USER_NAME IS NULL OR USER_NAME = '')";
            return await _db.ExecuteScalarAsync<int>(sql, new { UserName = userName });
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> MarkAsReadAsync(int notificationId)
    {
        try
        {
            var sql = "UPDATE DOIT_NOTIFIKASI SET IS_READ = 1 WHERE ID = @Id";
            var rows = await _db.ExecuteAsync(sql, new { Id = notificationId });
            return rows > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MarkAllAsReadAsync(string? userName)
    {
        try
        {
            var sql = "UPDATE DOIT_NOTIFIKASI SET IS_READ = 1 WHERE (USER_NAME = @UserName OR USER_NAME IS NULL OR USER_NAME = '')";
            var rows = await _db.ExecuteAsync(sql, new { UserName = userName });
            return rows > 0;
        }
        catch
        {
            return false;
        }
    }
}
