using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace VarsityHub.Services;

/// <summary>
/// SMS via Twilio. Selected when Sms:Provider = "twilio".
/// </summary>
public sealed class TwilioSms(IConfiguration cfg) : ISmsSender
{
    private readonly string _sid = cfg["Sms:TwilioSid"] ?? throw new InvalidOperationException("Sms:TwilioSid not configured");
    private readonly string _token = cfg["Sms:TwilioToken"] ?? throw new InvalidOperationException("Sms:TwilioToken not configured");
    private readonly string _from = cfg["Sms:TwilioFrom"] ?? throw new InvalidOperationException("Sms:TwilioFrom not configured");

    public Task SendAsync(string phoneNumber, string body)
    {
        TwilioClient.Init(_sid, _token);
        return MessageResource.CreateAsync(
            to: new PhoneNumber(phoneNumber),
            from: new PhoneNumber(_from),
            body: body);
    }
}
