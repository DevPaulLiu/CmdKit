using System;

namespace CmdKit.Models;

public class CommandEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty; // display name
    public string Value { get; set; } = string.Empty; // encrypted or plain
    public string? Description { get; set; }
    public string Kind { get; set; } = "Command"; // free-form category
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    // Indicates the value was originally encrypted when exported (used only for cross-machine plaintext export restore).
    public bool? WasEncrypted { get; set; }

    public bool IsEncrypted => Value.StartsWith(SecretProtector.Prefix, StringComparison.Ordinal);
}
