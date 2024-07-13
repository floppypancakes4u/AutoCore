namespace AutoCore.Game.Structures.Mail;

public class MailListItem
{
    public long MailId { get; set; }
    public string Subject { get; set; }
    public string Message { get; set; }
    public string SenderName { get; set; }
    public long Money { get; set; }
    public long AttachmentId { get; set; }
    public sbyte ExtraInfo { get; set; }
    public long TimeRemaining { get; set; }
}
