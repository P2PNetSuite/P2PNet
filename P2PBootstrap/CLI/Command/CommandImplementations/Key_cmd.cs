using P2PBootstrap.CLI.Command.ICommand;
using P2PBootstrap.Encryption;
using P2PBootstrap.Encryption.Pgp;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace P2PBootstrap.CLI.Command.CommandImplementations
{
    public class Key_cmd : ICommand.ICommand
    {

        public string Command { get; set; }
        public string Description { get; set; }
        public string Help { get; set; }
        public Dictionary<string, ICommandArg> Args { get; set; }
        public Func<List<ICommandArg>, CommandResponse> CommandDelegate { get; set; }

        public Key_cmd()
        {
            Command = "key";
            Description = "Handles key generation and listing.";
            Help = "Key Command Help:\n\r" +
    "key gen - Generate a new PGP key pair using a seedphrase.\n\r     Usage: key gen [multiple words for seedphrase go here]\n\r" +
    "key list - List all PGP keys.\n\r" +
    "key setpassphrase - Set the new PGP passphrase used when signing.\n\r     Usage: key setpassphrase [passphrase]\n\r" +
    "key help - Display this help message.\n\r";
            Args = new Dictionary<string, ICommandArg>
            {
                {"gen", new CommandArg { Arg = "gen", Description = "Generate a new key", CommandArgDelegate = GenerateKey  } },
                {"list", new CommandArg { Arg = "list", Description = "List all PGP keys", CommandArgDelegate = ListKeys } },
                { "setpassphrase", new CommandArg { Arg = "setpassphrase", Description = "Set the new PGP passphrase", CommandArgDelegate = SetPassphrase } },
                { "help", new CommandArg {Arg = "help", Description = "Display help message", CommandArgDelegate = HelpPage} }
            };

            CommandDelegate = ExecuteCommand;
        }

        public CommandResponse HelpPage(string arg)
        {

            return new CommandResponse { Response = Help, Success = true };
        }

        public CommandResponse ExecuteCommand(List<ICommandArg> args)
        {
            if (args == null || args.Count == 0)
            {
                DebugMessage("No arguments provided.");
                return new CommandResponse { Success = false, Response = "No arguments provided." };
            }

            List<string> tokens = new List<string>();
            foreach (var arg in args)
            {
                // split each argument on whitespace
                tokens.AddRange(arg.Arg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            if (tokens.Count == 0)
            {
                DebugMessage("No arguments provided after tokenization.");
                return new CommandResponse { Success = false, Response = "No arguments provided." };
            }

            // first token is the subcommand - remaining tokens are further params
            string subCommand = tokens[0];
            string parameters = tokens.Count > 1 ? string.Join(" ", tokens.Skip(1)) : string.Empty;

            if (Args.ContainsKey(subCommand))
            {
                var commandArg = Args[subCommand];
                var commandResponse = commandArg.CommandArgDelegate?.Invoke(parameters);
                return commandResponse;
            }
            else
            {
                DebugMessage($"Unknown argument: {subCommand}", MessageType.Warning);
                return new CommandResponse { Success = false, Response = $"Unknown argument: {subCommand}" };
            }
        }

        private CommandResponse GenerateKey(string arg)
        {
            // Implement key generation logic here
            CommandResponse cr = new CommandResponse();
            EncryptionService.GenNewPGPKey(arg, ref cr);
            return cr;
        }

        private CommandResponse ListKeys(string arg)
        {   
            return new CommandResponse { Response = PgpService.AllKeysList, Success = true };
        }

        private CommandResponse SetPassphrase(string arg)
        {
            // Call the PGP service to set the passphrase
            PgpService.SetPGPPassphrase(arg);
            return new CommandResponse
            {
                Success = true,
                Response = $"PGP passphrase successfully set."
            };
        }
    }
}
