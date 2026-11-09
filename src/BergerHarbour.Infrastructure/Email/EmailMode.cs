namespace BergerHarbour.Infrastructure.Email;

/// <summary><c>Smtp</c> sends through Gmail; <c>File</c> (the local default) writes emails to a folder.</summary>
public enum EmailMode
{
    Smtp,
    File,
}
