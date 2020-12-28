using System;
using System.Collections.Generic;
using System.Dynamic;
using JetBrains.Annotations;

namespace APNS.Models
{
    public class ApnsPush
    {
        public string Token { get; private set; }
        public string VoipToken { get; private set; }
        public int Priority => CustomPriority ?? (Type == ApnsPushType.Background ? 5 : 10);
        public ApnsPushType Type { get; }
        public int? CustomPriority { get; private set; }

        [CanBeNull] 
        public ApnsPushAlert Alert { get; private set; }

        public int? Badge { get; private set; }

        [CanBeNull] 
        public string Sound { get; private set; }
        
        [CanBeNull]
        public string Category { get; private set; }
        
        public bool IsContentAvailable { get; private set; }
        
        public bool IsMutableContent { get; private set; }
        
        public DateTimeOffset? Expiration { get; private set; }
        
        public Dictionary<string, object> CustomProperties { get; set; }
        
        public IDictionary<string, object> CustomApsProperties { get; set; }
        
        private bool _sendAlertAsText;

        public ApnsPush(ApnsPushType pushType)
        {
            Type = pushType;
        }

        public ApnsPush AddContentAvailable()
        {
            IsContentAvailable = true;
            return this;
        }

        public ApnsPush AddMutableContent()
        {
            IsMutableContent = true;
            return this;
        }

        public ApnsPush AddAlert([CanBeNull] string title, [CanBeNull] string subtitle, [NotNull] string body)
        {
            Alert = new ApnsPushAlert(title, subtitle, body);
            if (title == null)
                _sendAlertAsText = true;

            return this;
        }

        public ApnsPush AddAlert([CanBeNull] string title, [NotNull] string body)
        {
            Alert = new ApnsPushAlert(title, body);

            if (title == null)
                _sendAlertAsText = true;

            return this;
        }

        public ApnsPush AddAlert([NotNull] string body)
        {
            Alert = new ApnsPushAlert(null, body);

            return this;
        }

        public ApnsPush SetPriority(int priority)
        {
            if (priority < 0 || priority > 10)
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be between 0 and 10.");

            CustomPriority = priority;

            return this;
        }

        public ApnsPush AddBadge(int badge)
        {
            IsContentAvailableGuard();
            if (Badge != null)
                throw new InvalidOperationException("Badge already exists, you can not set multiple badges.");

            Badge = badge;

            return this;
        }
        
        public ApnsPush AddSound([NotNull] string sound = "default")
        {
            if (string.IsNullOrWhiteSpace(sound))
                throw new ArgumentException("Value cannot be null or contain whitespace.", nameof(sound));
            
            IsContentAvailableGuard();
            
            if (Sound != null)
                throw new InvalidOperationException("Sound already exists, you can not set multiple sounds.");
            
            Sound = sound;
            
            return this;
        }
        
        public ApnsPush AddCategory([NotNull] string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Value cannot be null or contain whitespace.", nameof(category));
            
            if (Category != null)
                throw new InvalidOperationException($"{nameof(Category)} already exists, you can not set multiple categories.");
            
            Category = category;
            
            return this;
        }
        
        public ApnsPush AddExpiration(DateTimeOffset expirationDate)
        {
            Expiration = expirationDate;
            
            return this;
        }
        
        public ApnsPush AddImmediateExpiration()
        {
            Expiration = DateTimeOffset.MinValue;
            
            return this;
        }

        public ApnsPush AddToken([NotNull] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(token));
            
            EnsureTokensNotExistGuard();
            
            if (Type == ApnsPushType.Voip)
                throw new InvalidOperationException($"Please use AddVoipToken() when sending {nameof(ApnsPushType.Voip)} pushes.");
            
            Token = token;
            
            return this;
        }

        public ApnsPush AddVoipToken([NotNull] string voipToken)
        {
            if (string.IsNullOrWhiteSpace(voipToken))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(voipToken));
            
            EnsureTokensNotExistGuard();
            
            if(Type != ApnsPushType.Voip)
                throw new InvalidOperationException($"VoIP token may only be used with {nameof(ApnsPushType.Voip)} pushes.");
            
            VoipToken = voipToken;
            
            return this;
        }
        
        public ApnsPush AddCustomProperty(string key, object value, bool addToApsDict = false)
        {
            if (addToApsDict)
            {
                CustomApsProperties ??= new Dictionary<string, object>();
                CustomApsProperties.Add(key, value);
            }
            else
            {
                CustomProperties ??= new Dictionary<string, object>();
                CustomProperties.Add(key, value);
            }
            return this;
        }

        private void EnsureTokensNotExistGuard()
        {
            if (!(string.IsNullOrEmpty(Token) && string.IsNullOrEmpty(VoipToken)))
                throw new InvalidOperationException("Notification already has token");
        }

        private void IsContentAvailableGuard()
        {
            if (IsContentAvailable)
                throw new InvalidOperationException("Cannot add fields to a push with content-available");
        }

        public object GeneratePayload()
        {
            dynamic payload = new ExpandoObject();
            payload.aps = new ExpandoObject();
            IDictionary<string, object> apsAsDict = payload.aps;
            
            if (IsContentAvailable)
                apsAsDict["content-available"] = "1";
            if(IsMutableContent)
                apsAsDict["mutable-content"] = "1";

            if (Alert != null)
            {
                object alert;
                if (_sendAlertAsText)
                    alert = Alert.Body;
                else if (Alert.Subtitle == null)
                    alert = new { title = Alert.Title, body = Alert.Body };
                else
                    alert = new { title = Alert.Title, subtitle = Alert.Subtitle, body = Alert.Body };
                payload.aps.alert = alert;
            }

            if (Badge != null)
                payload.aps.badge = Badge.Value;

            if (Sound != null)
                payload.aps.sound = Sound;

            if (Category != null)
                payload.aps.category = Category;

            if (CustomProperties != null)
            {
                IDictionary<string, object> payloadAsDict = payload;
                foreach (var customProperty in CustomProperties) 
                    payloadAsDict[customProperty.Key] = customProperty.Value;
            }

            if (CustomApsProperties != null)
            {
                foreach (var customApsProperty in CustomApsProperties)
                    apsAsDict[customApsProperty.Key] = customApsProperty.Value;
            }

            return payload;
        }
    }
}