// Tiny local site for trying the extension by hand: node extension/test/login-page.mjs → http://localhost:8765/login
import { createServer } from 'node:http';

const page = (title, body) => `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${title}</title>
<style>body{font:15px system-ui;max-width:360px;margin:60px auto} label{display:block;margin:12px 0 4px}
input{width:100%;padding:8px;font:inherit} button{margin-top:16px;padding:8px 16px;font:inherit}</style></head>
<body><h1>${title}</h1>${body}</body></html>`;

createServer((req, res) => {
  res.setHeader('Content-Type', 'text/html; charset=utf-8');
  if (req.url.startsWith('/welcome')) {
    res.end(page('Welcome', '<p>You are signed in.</p><p><a href="/login">Back to login</a></p>'));
    return;
  }
  res.end(page('Sign in to Test Shop', `
    <form method="post" action="/welcome">
      <label for="email">Email</label><input id="email" name="email" type="email" autocomplete="username">
      <label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password">
      <button type="submit">Sign in</button>
    </form>`));
}).listen(8765, () => console.log('http://localhost:8765/login'));
