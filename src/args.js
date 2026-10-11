// Minimal CLI-style argument parsing for chat commands: quotes, --flags and -f aliases.

// WhatsApp keyboards often turn " into typographic quotes, so all of them count.
const QUOTE = /["“”„]/;

// Splits text on whitespace; quoted parts stay together as one token.
function tokenize(text) {
  const tokens = [];
  let current = '';
  let inToken = false;
  let quoted = false;
  for (const ch of text.trim()) {
    if (QUOTE.test(ch)) {
      quoted = !quoted;
      inToken = true;
    } else if (!quoted && /\s/.test(ch)) {
      if (inToken) tokens.push(current);
      current = '';
      inToken = false;
    } else {
      current += ch;
      inToken = true;
    }
  }
  if (inToken) tokens.push(current);
  return tokens;
}

// iOS smart punctuation turns "--" into "—" or "–", so accept those as well.
function flagName(token) {
  const match = token.match(/^(?:--|—|–)([a-z][\w-]*)$/i) || token.match(/^-([a-z])$/i);
  return match ? match[1].toLowerCase() : null;
}

// Tokens after a flag belong to that flag until the next flag.
function parseArgs(tokens, aliases = {}) {
  const positional = [];
  const flags = {};
  let target = positional;
  for (const token of tokens) {
    const flag = flagName(token);
    if (flag) {
      const name = aliases[flag] || flag;
      flags[name] = flags[name] || [];
      target = flags[name];
    } else {
      target.push(token);
    }
  }
  return { positional, flags };
}

module.exports = { tokenize, parseArgs };
