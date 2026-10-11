// Persists the state of all groups in a JSON file.
const fs = require('fs');
const path = require('path');

function emptyGroup() {
  return { members: [], expenses: [], nextId: 1 };
}

function createStore(filePath) {
  let data = {};
  if (filePath && fs.existsSync(filePath)) {
    data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
  }

  return {
    get(groupId) {
      if (!data[groupId]) data[groupId] = emptyGroup();
      return data[groupId];
    },
    reset(groupId) {
      data[groupId] = emptyGroup();
    },
    save() {
      if (!filePath) return; // In tests without a file
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      const tmp = `${filePath}.tmp`;
      fs.writeFileSync(tmp, JSON.stringify(data, null, 2));
      fs.renameSync(tmp, filePath);
    },
  };
}

module.exports = { createStore };
