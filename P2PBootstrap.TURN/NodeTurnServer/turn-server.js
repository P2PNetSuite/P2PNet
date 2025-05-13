   // NodeTurnServer/turn-server.js
   const Turn = require('node-turn');
   const argv = require('yargs').argv;

   const server = new Turn({
     listeningPort: argv.port || 3478,
     authMech: 'long-term',
     credentials: {
       [argv.user || 'user']: argv.pass || 'pass'
     }
   });
   server.start();
   console.log('TURN server running on port', argv.port || 3478);