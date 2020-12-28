using System;
using JetBrains.Annotations;

namespace APNS
{
    public class ApnsJwtOptions
    {
        [CanBeNull]
        public string CertFilePath
        {
            get => _certFilePath;
            set
            {
                if (value != null && CertContent != null)
                    throw new InvalidOperationException("Either path to the certificate or certificate's contents must be provided, not both.");
                _certFilePath = value;
            }
        }

        [CanBeNull] 
        private string _certFilePath;
        
        [CanBeNull]
        public string CertContent
        {
            get => _certContent;
            set
            {
                if (value != null && CertFilePath != null)
                    throw new InvalidOperationException("Either path to the certificate or certificate's contents must be provided, not both.");
                _certContent = value;
            }
        }

        [CanBeNull] 
        private string _certContent;

        [NotNull]
        public string KeyId
        {
            get => _keyId;
            set => _keyId = value ?? throw new ArgumentNullException(nameof(KeyId));
        }

        [NotNull] 
        private string _keyId;
        
        [NotNull]
        public string TeamId
        {
            get => _teamId;
            set => _teamId = value ?? throw new ArgumentNullException(nameof(TeamId));
        }

        [NotNull] 
        private string _teamId;
        
        [NotNull]
        public string BundleId
        {
            get => _bundleId;
            set => _bundleId = value ?? throw new ArgumentNullException(nameof(BundleId));
        }

        [NotNull] 
        private string _bundleId;
    }
}