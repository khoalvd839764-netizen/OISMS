const fs = require('node:fs');
const path = require('node:path');
// Reuse only the independent R1 transpile/hooks/storage harness, not its scenarios.
const harness = fs.readFileSync(path.join(__dirname, 'harness.cjs'), 'utf8');
const scenarios = String.raw`
(async () => {
  // Controls: retry, reset, HTTP 500, malformed response, mounted reload.
  for (const outcome of ['lost', '500']) {
    const server = transport(outcome), env = environment(server.fetch), api = apiFor(env);
    const m = modal(env, api, {...props, currentUser: userA});
    await checkout(m);
    const kept = api.getPendingCheckout(userA, branchA);
    const reset = find(m.render(), n => n.type === 'button' && n.props.onClick?.name === 'handleResetPending');
    assert.ok(reset); reset.props.onClick();
    await checkout(m);
    assert.ok(kept); assert.equal(server.orders.size, 1);
    assert.equal(server.calls[0].requestKey, server.calls[1].requestKey);
    observations.push({scenario: outcome + ': reset then retry', pendingKept: true, sameKey: true, modeledOrders: server.orders.size});
  }
  for (const kind of ['parse-error', 'missing-order']) {
    let printed = 0;
    const env = environment(async () => ({ok: true, status: 200, json: async () => {
      if (kind === 'parse-error') throw new SyntaxError('Injected invalid JSON');
      return {};
    }}));
    const api = apiFor(env), m = modal(env, api, {...props, currentUser: userA, onSuccessAndPrint: () => printed++});
    await checkout(m);
    assert.equal(printed, 0); assert.ok(api.getPendingCheckout(userA, branchA));
    observations.push({scenario: kind, printedOfficialReceipt: printed, pendingKept: true});
  }
  {
    const server = transport(), env = environment(server.fetch), api = apiFor(env);
    const m = modal(env, api, {...props, currentUser: userA}); await checkout(m);
    api.updatePendingCheckoutStatus('submitting', undefined, undefined, userA, branchA);
    const a = app(env, apiFor(env), [{id: branchB, name: 'B'}, {id: branchA, name: 'A'}]);
    await Promise.resolve(); await Promise.resolve();
    assert.equal(a.state[3], branchA); assert.equal(a.state[1].length, 1);
    assert.equal(api.getPendingCheckout(userA, branchA).status, 'unknown');
    observations.push({scenario: 'Reload with submitting and B listed first', branchRecovered: a.state[3], cartRows: a.state[1].length, status: api.getPendingCheckout(userA, branchA).status});
  }
  // F01 counterexample: the same mounted modal survives closing/auth/branch updates.
  for (const switchKind of ['different-user', 'different-tenant']) {
    const server = transport(); const calls = [];
    const env = environment(async (url, init) => {
      calls.push({authorization: init.headers.Authorization, body: JSON.parse(init.body)});
      return server.fetch(url, init);
    });
    const api = apiFor(env);
    const a = app(env, api);
    await Promise.resolve();
    const input = {...props, items: [...props.items], currentUser: userA};
    const m = modal(env, api, input); await checkout(m);
    const original = api.getPendingCheckout(userA, branchA);
    input.isOpen = false; m.render();
    const nextUser = switchKind === 'different-tenant' ? {...userB, tenantId: 'tenant-b'} : userB;
    api.setStoredUser(nextUser); api.setAuthToken('fixture-token-B');
    find(a.render(), n => n.type === a.components.LoginModal).props.onLoginSuccess(nextUser);
    await Promise.resolve();
    const pm = find(a.render(), n => n.type === a.components.PaymentModal);
    Object.assign(input, pm.props, {isOpen: true});
    const search = find(a.render(), n => n.type === a.components.ProductSearch);
    search.props.onAddToCart({...props.items[0], variantId: '44444444-4444-4444-8444-444444444444', sku: 'SKU-B', productName: 'User B product', onHand: 10, reserved: 0});
    Object.assign(input, find(a.render(), n => n.type === a.components.PaymentModal).props, {isOpen: true});
    assert.equal(input.currentUser.id, nextUser.id);
    const warning = find(m.render(), n => n.type === 'code' && n.props.children === original.requestKey);
    await checkout(m);
    assert.equal(calls[1].authorization, 'Bearer fixture-token-B');
    assert.equal(calls[1].body.requestKey, original.requestKey);
    assert.equal(calls[1].body.items[0].skuCode, 'SKU-A');
    observations.push({scenario: 'Persistent modal after ' + switchKind, storageLookupForB: Boolean(api.getPendingCheckout(nextUser, branchA)), oldKeyDisplayed: Boolean(warning), currentCartSku: input.items[0].sku, submittedSku: calls[1].body.items[0].skuCode, oldKeySentWithNewToken: calls[1].body.requestKey === original.requestKey, authorization: calls[1].authorization});
  }
  // F01 counterexample: invoke the production LoginModal submit, not a fabricated rich callback.
  {
    let checkoutCalls = 0;
    const env = environment(async (url, init) => {
      if (url.endsWith('/api/auth/login')) return response(200, {accessToken: 'fixture-relogin-token', user: userA});
      checkoutCalls++; throw new TypeError('Injected response loss');
    });
    const api = apiFor(env), a = app(env, api);
    await Promise.resolve();
    const create = modal(env, api, {...props, currentUser: userA}); await checkout(create);
    // The existing cart is set through the public UI handler before expiration.
    a.state[1] = [...props.items];
    api.removeAuthToken(); env.window.dispatchEvent(new env.CustomEvent('pos-auth-expired')); a.render();
    const loginProps = find(a.render(), n => n.type === a.components.LoginModal).props;
    const l = mount('src/components/LoginModal.tsx', 'LoginModal', env, {'../services/api': api}, loginProps);
    let tree = l.render();
    find(tree, n => n.type === 'input' && n.props.type === 'email').props.onChange({target: {value: userA.email}});
    find(tree, n => n.type === 'input' && n.props.type === 'password').props.onChange({target: {value: 'FixtureOnlyPassword'}});
    await find(l.render(), n => n.type === 'form').props.onSubmit({preventDefault(){}});
    await Promise.resolve(); await Promise.resolve();
    const activeUser = a.state[0], persistedUser = api.getStoredUser();
    assert.equal(persistedUser.tenantId, userA.tenantId);
    assert.equal(activeUser.tenantId, undefined);
    assert.equal(a.state[1].length, 0);
    const validPending = api.getPendingCheckout(userA, branchA);
    assert.ok(validPending); assert.equal(api.getPendingCheckout(activeUser, branchA), null);
    observations.push({scenario: 'Same-user reauthentication through actual LoginModal', callbackUser: activeUser, storedTenantId: persistedUser.tenantId, cartRowsAfterLogin: a.state[1].length, pendingAccessibleForUi: Boolean(api.getPendingCheckout(activeUser, branchA)), originalPendingRetained: Boolean(validPending)});
    // Start another checkout through App's public add handler under the sparse callback identity.
    find(a.render(), n => n.type === a.components.ProductSearch).props.onAddToCart({...props.items[0], onHand:10, reserved:0});
    const p = {...find(a.render(), n => n.type === a.components.PaymentModal).props, isOpen: true};
    const freshModal = modal(env, api, p); await checkout(freshModal);
    const records = JSON.parse(env.session.get('pos_pending_checkout_v1')).records;
    const sparse = records.find(r => !r.owner.tenantId && !r.owner.tenantCode);
    assert.ok(sparse); assert.notEqual(sparse.requestKey, validPending.requestKey);
    observations.push({scenario: 'New attempt after real relogin', checkoutCalls, newKeyDifferentFromOriginal: sparse.requestKey !== validPending.requestKey, newOwner: sparse.owner, newAttemptLookupForUi: Boolean(api.getPendingCheckout(activeUser, branchA))});
  }
  const result = {provider:'Actual production TS/TSX under Node VM with hook, storage, events and fetch stubs; no browser/HTTP/PostgreSQL', observations};
  fs.writeFileSync(path.join(__dirname, 'probe-results.json'), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
})().catch(error => {console.error(error); process.exitCode = 1;});
`;
new Function('require', '__dirname', harness + scenarios)(require, __dirname);
