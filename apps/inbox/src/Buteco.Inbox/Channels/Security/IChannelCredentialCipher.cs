namespace Buteco.Inbox.Channels.Security;

public interface IChannelCredentialCipher
{
    string Encrypt(string plaintext);

    string Decrypt(string stored);
}
