using System.Collections;
using Org.BouncyCastle.Pqc.Crypto.Lms;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using PgpCore;
using Org.BouncyCastle.Bcpg.OpenPgp;
using P2PBootstrap.Encryption.Pgp.Keys;
using System.Text;
using Microsoft.Extensions.Primitives;


namespace P2PBootstrap.Encryption.Pgp
    {
    public static class PgpService
        {
        private static PGP localPGP = new PGP();
        private static string KeysDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, GlobalConfig.KeysDirectory());
        private static KeyPair ActiveKeyPair => GlobalConfig.ActiveKeys;
        private static List<PGPKeyInfo> _pgpKeys = new List<PGPKeyInfo>();
        private static bool firstSet = false;
        private static bool PrivateKeySet => ActiveKeyPair.Private != null;
        private static bool PublicKeySet => ActiveKeyPair.Public != null;

        // TODO implement public accessors, advise user to implement a unique salt in documentation
        private static byte[] hashSalt = new byte[]
                               {
                                       0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66,
                                       0x77, 0x88
                               };
        
        public static string AllKeysList => string.Join(Environment.NewLine, _pgpKeys.Select(k => k.Name).ToArray());

        public static string CurrentPGPPassphrase => _passphrase;
        private static string _passphrase = "password";

        public static void Initialize()
        {
            // Ensure the keys directory exists
            if (!Directory.Exists(KeysDirectory))
            {
                Directory.CreateDirectory(KeysDirectory);
            }

            if (GlobalConfig.TrustPolicy() == TrustPolicies.BootstrapTrustPolicyType.Authority)
            {
                string publicKeyPath = Path.Combine(KeysDirectory, GlobalConfig.PublicKeyPath());
                string privateKeyPath = Path.Combine(KeysDirectory, GlobalConfig.PrivateKeyPath());

                // Check if the public and private key files exist
                if (!File.Exists(publicKeyPath) || !File.Exists(privateKeyPath))
                {
                    // Generate PGP key pair if the keys do not exist
                    string message;
                    bool success = GeneratePGPKeyPair("default-seedphrase", out message, "public", "private");
                    if (!success)
                    {
                        DebugMessage($"Failed to generate PGP key pair: {message}", MessageType.Critical);
                        return;
                    }
                }
                else 
                {
                    firstSet = true;
                }

                LoadPGPKeysFromDirectory();
                GlobalConfig.SetTargetKeys();
            }
        }
        public static bool GeneratePGPKeyPair(string seedphrase, out string message, string pub = "public", string priv = "private")
        {
                if ((seedphrase.Length > 128) || (seedphrase.Length < 8))
                {
                    message = "PGP seedphrase must be between 8 and 128 character.";
                    DebugMessage(message, MessageType.Warning);
                    return false;
                }
                else
                {
                
                // Determine unique file names
                string publicKeyFile = Path.Combine(KeysDirectory, $"{pub}.asc");
                string privateKeyFile = Path.Combine(KeysDirectory, $"{priv}.asc");
                int index = 1;
                while (File.Exists(publicKeyFile) && File.Exists(privateKeyFile))
                {
                    publicKeyFile = Path.Combine(KeysDirectory, $"{pub}({index}).asc");
                    privateKeyFile = Path.Combine(KeysDirectory, $"{priv}({index}).asc");
                    index++;
                }

                // Create new FileInfo objects to coorespond with the public/private key file paths
                FileInfo pubKeyPath = new FileInfo(publicKeyFile);
                FileInfo privKeyPath = new FileInfo(privateKeyFile);

                    // Generate new PGP key pair
                    // Derive Passphrase from Seed Phrase (Important for Security)
                    using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(seedphrase,
                               salt: hashSalt, iterations: 10000))
                    {
                        string passphrase = Convert.ToBase64String(pbkdf2.GetBytes(32)); // 32 bytes for a strong key

                    try {
                        // Generate Keys 
                        localPGP.GenerateKey(pubKeyPath, privKeyPath, GlobalConfig.NetworkName(), passphrase, emitVersion: false);
                    }
                    catch (Exception ex) {
                        message = "PGP key generation failed: " + ex.Message;
                        DebugMessage(message, MessageType.Critical);
                        return false;
                    }

                        // load all keys into memory
                        LoadPGPKeysFromDirectory();

                        Console.WriteLine("A private passphrase was generated with this PGP key pair. It is important to keep this passphrase secure and private, and to not lose it. You will need to use it in order to properly sign messages and encrypt data with your private key. It is advised that you write it down and store it somewhere safe..");
                        Console.WriteLine("Passphrase: " + passphrase);
                        message = $"PGP key pair generated successfully. Save this passphrase: {passphrase}";
                        if(firstSet == false)
                        {
                            _passphrase = passphrase;
                            firstSet = true;
                        }
                        return true;
                    }

                }
        }

        public static void LoadPGPKeysFromDirectory()
        {
            // We will locate every *.asc key in the keysDirectory folder, and for each key found we will
            // create an instance of PGPKeyInfo, then add it to _pgpKeys
            _pgpKeys.Clear(); // Clear existing keys

            // Get all *.asc files
            var keyFiles = Directory.GetFiles(KeysDirectory, "*.asc");

            // Read key data and add to the list
            foreach (var file in keyFiles)
            {
                var keyData = File.ReadAllBytes(file);
                var keyName = Path.GetFileNameWithoutExtension(file);
                _pgpKeys.Add(new PGPKeyInfo { Name = keyName, KeyData = keyData });
            }

        }

        public static Task<bool> ClearSignString(ref string message)
        {
            if (!PrivateKeySet)
            {
                DebugMessage( "No private key set.", MessageType.Warning);
                return Task.FromResult(false);
            }

            bool flawless_ = true;

            try
            {
                    string prvkey = Encoding.UTF8.GetString(ActiveKeyPair.Private.KeyData);

                    EncryptionKeys encyptionKeys = new EncryptionKeys(prvkey, _passphrase);

                    PGP temppgp = new PGP(encyptionKeys);

                    string msgout = temppgp.ClearSign(message);

                    message = msgout;
            }
            catch(Exception ex)
            {
                    DebugMessage(ex.ToString(), MessageType.Warning);
            }

            return Task.FromResult(false);

        }

        public static void SetPGPPassphrase(string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                _passphrase = passphrase;
                return;
            }
            
        }

    }

}
  