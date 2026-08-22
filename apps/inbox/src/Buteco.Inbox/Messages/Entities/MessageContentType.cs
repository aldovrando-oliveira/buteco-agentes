namespace Buteco.Inbox.Messages.Entities;

// Marcador de tipo — mídia binária não é persistida nesta fatia (design.md,
// Decisão 8). Vídeo e outros tipos não modelados caem em Document.
public enum MessageContentType
{
    Text,
    Image,
    Audio,
    Document,
}
