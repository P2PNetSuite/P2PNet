using P2PBootstrap.Encryption.Pgp;
using P2PBootstrap.Encryption.Pgp.Keys;

namespace P2PBootstrap
{
    public static class GlobalConfig
    {
        /// <summary>
        /// The application configuration loaded from appsettings.json.
        /// </summary>
        public static IConfiguration AppSettings;

        /// <summary>
        /// The default configuration file name.
        /// </summary>
        public const string ConfigFile = "appsettings.json";

        /// <summary>
        /// Indicates whether the application is running in a containerized environment.
        /// </summary>
        public static bool _containerized = false;

        /// <summary>
        /// The active key pair used for cryptographic operations.
        /// </summary>
        public static KeyPair ActiveKeys { get; set; } = new KeyPair();

        /// <summary>
        /// Sets the target keys in GlobalConfig to the keys specified in appsettings.json.
        /// </summary>
        public static void SetTargetKeys()
        {
            string keysDirectory = AppSettings["Configuration:KeysDirectory"];
            string publicKeyPath = Path.Combine(AppContext.BaseDirectory, keysDirectory, AppSettings["Configuration:AuthorityKey:PublicKey"]);
            string privateKeyPath = Path.Combine(AppContext.BaseDirectory, keysDirectory, AppSettings["Configuration:AuthorityKey:PrivateKey"]);
            if (File.Exists(publicKeyPath) && File.Exists(privateKeyPath))
            {
                byte[] publicKeyData = File.ReadAllBytes(publicKeyPath);
                byte[] privateKeyData = File.ReadAllBytes(privateKeyPath);

                ActiveKeys = new KeyPair(
                    new PGPKeyInfo(Path.GetFileNameWithoutExtension(publicKeyPath), publicKeyData),
                    new PGPKeyInfo(Path.GetFileNameWithoutExtension(privateKeyPath), privateKeyData)
                );

            }
            else
            {
                DebugMessage("Public or private key file not found.", MessageType.Warning);
            }
        }

        public static void CheckContainerEnvironment()
        {
            string containerEnv = Environment.GetEnvironmentVariable("CONTAINERIZED_ENVIRONMENT", EnvironmentVariableTarget.Process);
            if (!string.IsNullOrEmpty(containerEnv) && containerEnv.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                _containerized = true;
            }
        }

        #region GlobalConfig Values

        /// <summary>
        /// Gets the directory where key files are stored.
        /// Checks environment variable KEYS_DIRECTORY if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string KeysDirectory()
        {
            string ENVVAR = "KEYS_DIRECTORY";
            if (!_containerized)
                {
                    // Non-containerized mode: read from appsettings.json
                    return AppSettings["Configuration:KeysDirectory"];
                }
                else
                {
                    // Containerized mode: check environment variable, or fall back to appsettings.json
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (envVar != null)
                    {
                        return envVar;
                    }
                    return AppSettings["Configuration:KeysDirectory"];
                }
        }

        /// <summary>
        /// Gets the trust policy type for the bootstrap server.
        /// Checks environment variable BOOTSTRAP_MODE if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static TrustPolicies.BootstrapTrustPolicyType TrustPolicy()
        {
            string ENVVAR = "BOOTSTRAP_MODE";
            if (!_containerized)
            {
                string _ = AppSettings["Configuration:BootstrapMode"];
                if (_.Equals("Authority", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustPolicies.BootstrapTrustPolicyType.Authority;
                }
                if (_.Equals("Trustless", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustPolicies.BootstrapTrustPolicyType.Trustless;
                }

                throw new KeyNotFoundException("BootstrapMode not found in appsettings.json. Please set it to either 'Authority' or 'Trustless'.");
            }
            else
            {
                string bootstrapModeVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                if (bootstrapModeVar != null)
                {
                    if (bootstrapModeVar.Equals("Authority", StringComparison.OrdinalIgnoreCase))
                    {
                        return TrustPolicies.BootstrapTrustPolicyType.Authority;
                    }
                    if (bootstrapModeVar.Equals("Trustless", StringComparison.OrdinalIgnoreCase))
                    {
                        return TrustPolicies.BootstrapTrustPolicyType.Trustless;
                    }
                    throw new InvalidDataException($"Invalid value for {ENVVAR}. Expected 'Authority' or 'Trustless', but got '{bootstrapModeVar}'.");
                }
                else
                {
                    // defer back to config file
                    string _ = AppSettings["Configuration:BootstrapMode"];
                    if (_.Equals("Authority", StringComparison.OrdinalIgnoreCase))
                    {
                        return TrustPolicies.BootstrapTrustPolicyType.Authority;
                    }
                    if (_.Equals("Trustless", StringComparison.OrdinalIgnoreCase))
                    {
                        return TrustPolicies.BootstrapTrustPolicyType.Trustless;
                    }

                    throw new KeyNotFoundException($"BootstrapMode not found in appsettings.json, nor was it set as environmental variable {ENVVAR} for the container. Please set it to either 'Authority' or 'Trustless'.");

                }
            }

        }

        /// <summary>
        /// Gets the full path to the public key file.
        /// Checks environment variable PUBLIC_KEY_PATH if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string PublicKeyPath()
        {
            string ENVVAR = "PUBLIC_KEY_PATH";
            if (!_containerized)
                {
                    return Path.Combine(AppContext.BaseDirectory, KeysDirectory(), AppSettings["Configuration:AuthorityKey:PublicKey"]);
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (envVar != null)
                    {
                        return envVar;
                    }
                    return Path.Combine(AppContext.BaseDirectory, KeysDirectory(), AppSettings["Configuration:AuthorityKey:PublicKey"]);
                }
        }

        /// <summary>
        /// Gets the full path to the private key file.
        /// Checks environment variable PRIVATE_KEY_PATH if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string PrivateKeyPath()
        {
            string ENVVAR = "PRIVATE_KEY_PATH";
            if (!_containerized)
                {
                    return Path.Combine(AppContext.BaseDirectory, KeysDirectory(), AppSettings["Configuration:AuthorityKey:PrivateKey"]);
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (envVar != null)
                    {
                        return envVar;
                    }
                    return Path.Combine(AppContext.BaseDirectory, KeysDirectory(), AppSettings["Configuration:AuthorityKey:PrivateKey"]);
                }
        }

        /// <summary>
        /// Gets the configured network name.
        /// Checks environment variable NETWORK_NAME if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string NetworkName()
        {
            // Matches the 'Configuration:NetworkName' value in appsettings.json
            string ENVVAR = "NETWORK_NAME";
            if (!_containerized)
            {
                // Non-containerized mode: read directly from appsettings.json
                return AppSettings["Configuration:NetworkName"];
            }
            else
            {
                // Containerized mode: check environment variable first
                string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrEmpty(envVar))
                {
                    return envVar;
                }
                // If no environment variable is found, defer back to appsettings.json
                return AppSettings["Configuration:NetworkName"];
            }
        }


        /// <summary>
        /// Provides configuration options for optional HTTP endpoints.
        /// </summary>
        public static class OptionalEndpoints
        {
            /// <summary>
            /// Determines if the server should serve its public IP endpoint.
            /// Checks environment variable ENDPOINT_PUBLICIP if containerized, otherwise uses appsettings.json.
            /// </summary>
            public static bool ServePublicIP()
            {
                string ENVVAR = "ENDPOINT_PUBLICIP";
                if (!_containerized)
                {
                    string configValue = AppSettings["Configuration:OptionalEndpoints:PublicIP"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (!string.IsNullOrEmpty(envVar))
                    {
                        return bool.TryParse(envVar, out bool envResult) && envResult;
                    }
                    string configValue = AppSettings["Configuration:OptionalEndpoints:PublicIP"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
            }
        }

        /// <summary>
        /// Provides configuration options for optional network services.
        /// </summary>
        public static class OptionalServices
        {
            /// <summary>
            /// Determines if the WebRTC service is enabled.
            /// Checks environment variable OPTIONALSERVICE_WEBRTC if containerized, otherwise uses appsettings.json.
            /// </summary>
            public static bool WebRTC()
            {
                string ENVVAR = "OPTIONALSERVICE_WEBRTC";
                if (!_containerized)
                {
                    string configValue = AppSettings["Configuration:OptionalServices:WebRTC"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (!string.IsNullOrEmpty(envVar))
                    {
                        return bool.TryParse(envVar, out bool envResult) && envResult;
                    }
                    string configValue = AppSettings["Configuration:OptionalServices:WebRTC"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
            }

            /// <summary>
            /// Determines if UDP NAT hole punching is enabled.
            /// Checks environment variable OPTIONALSERVICE_UDPNATHOLEPUNCH if containerized, otherwise uses appsettings.json.
            /// </summary>
            public static bool UDPNATHolepunch()
            {
                string ENVVAR = "OPTIONALSERVICE_UDPNATHOLEPUNCH";
                if (!_containerized)
                {
                    string configValue = AppSettings["Configuration:OptionalServices:UDPNATHolepunch"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (!string.IsNullOrEmpty(envVar))
                    {
                        return bool.TryParse(envVar, out bool envResult) && envResult;
                    }
                    string configValue = AppSettings["Configuration:OptionalServices:UDPNATHolepunch"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
            }

            /// <summary>
            /// Determines if TURN relay service is enabled.
            /// Checks environment variable OPTIONALSERVICE_TURN if containerized, otherwise uses appsettings.json.
            /// </summary>
            public static bool TURN()
            {
                string ENVVAR = "OPTIONALSERVICE_TURN";
                if (!_containerized)
                {
                    string configValue = AppSettings["Configuration:OptionalServices:TURN"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
                else
                {
                    string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                    if (!string.IsNullOrEmpty(envVar))
                    {
                        return bool.TryParse(envVar, out bool envResult) && envResult;
                    }
                    string configValue = AppSettings["Configuration:OptionalServices:TURN"];
                    return bool.TryParse(configValue, out bool configResult) && configResult;
                }
            }
        }

        /// <summary>
        /// Gets the database file name.
        /// Checks environment variable DB_FILENAME if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string DbFileName()
        {
            // Matches the 'Database:DbFileName' value in appsettings.json
            string ENVVAR = "DB_FILENAME";
            if (!_containerized)
            {
                // Non-containerized mode: read directly from appsettings.json
                return AppSettings["Database:DbFileName"];
            }
            else
            {
                // Containerized mode: check environment variable first
                string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrEmpty(envVar))
                {
                    return envVar;
                }
                // If no environment variable is found, defer back to appsettings.json
                return AppSettings["Database:DbFileName"];
            }
        }

        /// <summary>
        /// Gets the configured identifier for this node.
        /// Checks environment variable IDENTIFIER if containerized, otherwise uses appsettings.json.
        /// </summary>
        public static string ConfigIdentifier()
        {
            string ENVVAR = "IDENTIFIER";
            if (!_containerized)
            {
                // Non-containerized mode: read directly from appsettings.json
                return AppSettings["Configuration:Identifier"];
            }
            else
            {
                // Containerized mode: check environment variable first
                string envVar = Environment.GetEnvironmentVariable(ENVVAR, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrEmpty(envVar))
                {
                    return envVar;
                }
                // If no environment variable is found, defer back to appsettings.json
                return AppSettings["Configuration:Identifier"];
            }
        }

        #endregion

    }
}
