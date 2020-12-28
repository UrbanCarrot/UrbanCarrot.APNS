using System;
using JetBrains.Annotations;

namespace APNS.Models
{
    public class ApnsPushAlert
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string Body { get; }

        public ApnsPushAlert([CanBeNull] string title, [NotNull] string body)
        {
            Title = title;
            Body = body ?? throw new ArgumentException(nameof(body));
        }

        public ApnsPushAlert([CanBeNull] string title, [CanBeNull] string subtitle, [NotNull] string body)
        {
            Title = title;
            Subtitle = subtitle;
            Body = body ?? throw new ArgumentException(nameof(body));
        }
    }
}