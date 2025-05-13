const fs = require('fs');
const { execSync } = require('child_process');

function checkAndInstall(packageName) {
  if (!fs.existsSync(`node_modules/${packageName}`)) {
    console.log(`Installing ${packageName}...`);
    execSync(`npm install ${packageName}`, { stdio: 'inherit' });
  } else {
    console.log(`${packageName} is already installed.`);
  }
}

checkAndInstall('node-turn');
checkAndInstall('yargs');