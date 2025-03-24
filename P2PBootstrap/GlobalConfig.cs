using P2PBootstrap.Encryption.Pgp;
using P2PBootstrap.Encryption.Pgp.Keys;

namespace P2PBootstrap
{
    public static class GlobalConfig
    {
        public static IConfiguration AppSettings;
        public const string ConfigFile = "appsettings.json";
        public static bool _containerized = false;
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

        /// --- Optional Endpoints ---
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





        /// --------------------------

        #endregion

    }
}
