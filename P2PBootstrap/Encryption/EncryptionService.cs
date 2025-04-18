using PgpCore;
using System.Collections;
using Org.BouncyCastle.Pqc.Crypto.Lms;
using System.IO;
using P2PBootstrap.Encryption.Pgp;
using P2PBootstrap.CLI.Command;
using System.Runtime.CompilerServices;
using P2PNet.Distribution.NetworkTasks;
using System.Security.Cryptography;
using System.Text;

namespace P2PBootstrap.Encryption
    {
    public static class EncryptionService
        {
        private static MD5 hashing = MD5.Create();
        public static void Initialize()
            {
                PgpService.Initialize();
            }

            public static void GenNewPGPKey(string input, ref CommandResponse commandResponse)
            {
                bool valid = PgpService.GeneratePGPKeyPair(input, out string message);
                commandResponse.Response = message;
                commandResponse.Success = valid;
            }

        /// <summary>
        /// This will generate a unique hash of the NetworkTask.
        /// This hash will then be PGP clear signed, with the clear signed message then added to the NetworkTask under the "Signature" key.
        /// The hash and signature will then be recorded locally for reference.
        /// Recipients can then remove the "Signature" key, verify the clearn signed message, and verify the hash.
        /// </summary>
        public static void SignOffOnNetworkTask(ref NetworkTask networkTask)
            {
            // get hash of the NetworkTask in its current state
            string _hash = Convert.ToBase64String(hashing.ComputeHash(networkTask.ToByte()));

            // clear sign a message of this hash
            string _signature = $"{_hash}";
            PgpService.ClearSignString(ref _signature);

            // TODO if _signature still equals _hash then the ClearSign failed, likely because passphrase/priv key mismatch
            // implement logic to handle this

            // add the signature to the NetworkTask
            networkTask.TaskData.Add("Signature", _signature);

            // record the hash and signature locally for reference
            var dict = new Dictionary<string, string>
            {
                { "Hash", _hash },
                { "Signature", _signature }
            };
            Task.Run(() => { ExecuteTableCommand(SigningHistory_table.RunInsertCommand(dict)); });
        }

    }
    }