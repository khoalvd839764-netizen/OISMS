const fs = require('node:fs');
const path = require('node:path');
const harness = fs.readFileSync(path.join(__dirname, 'harness.cjs'), 'utf8');
const scenarios = String.raw`
async function loginThroughForm(a, env, api, user) {
  find(a.render(), n => n.type === a.components.PosHeader).props.onOpenLogin();
  const lp = find(a.render(), n => n.type === a.components.LoginModal).props;
  const l = mount('src/components/LoginModal.tsx', 'LoginModal', env, {'../services/api':api}, lp);
  find(l.render(), n => n.type === 'input' && n.props.type === 'email').props.onChange({target:{value:user.email}});
  find(l.render(), n => n.type === 'input' && n.props.type === 'password').props.onChange({target:{value:'FixtureOnlyPassword'}});
  await find(l.render(), n => n.type === 'form').props.onSubmit({preventDefault(){}});
  await Promise.resolve(); await Promise.resolve();
  assert.equal(a.state[0].id, user.id);
}
function inputOf(a) {
  return {...find(a.render(), n => n.type === a.components.PaymentModal).props, isOpen:true};
}
function addThroughSearch(a) {
  find(a.render(), n => n.type === a.components.ProductSearch).props.onAddToCart({...props.items[0], onHand:10, reserved:0});
}
(async () => {
  for (const pathKind of ['expiry-switch-back', 'two-owners-logout-relogin']) {
    const requests = [];
    const env = environment(async (url, init) => {
      const body = JSON.parse(init.body);
      if (url.endsWith('/api/auth/login')) {
        const user = body.email === userA.email ? userA : userB;
        return response(200, {accessToken:'fixture-token-'+user.id, user});
      }
      requests.push(body); throw new TypeError('Injected unknown checkout');
    });
    const api = apiFor(env);
    const a = app(env, api, [{id:branchB,name:'B'}, {id:branchA,name:'A'}]);
    await Promise.resolve(); await Promise.resolve();
    // Use only public App callbacks for branch/cart/logout/login changes.
    find(a.render(), n => n.type === a.components.PosHeader).props.onSelectBranch(branchA);
    addThroughSearch(a);
    const mA = modal(env, api, inputOf(a)); await checkout(mA);
    const pendingA = api.getPendingCheckout(userA, branchA); assert.ok(pendingA);
    find(a.render(), n => n.type === a.components.PosHeader).props.onLogout();
    assert.equal(a.state[0], null);
    await loginThroughForm(a, env, api, userB);
    assert.equal(a.state[3], branchB);
    assert.equal(a.state[1].length, 0);
    let targetUser, targetPending;
    if (pathKind === 'expiry-switch-back') {
      targetUser = userA; targetPending = pendingA;
      api.removeAuthToken(); env.window.dispatchEvent(new env.CustomEvent('pos-auth-expired'));
      await loginThroughForm(a, env, api, userA);
    } else {
      addThroughSearch(a);
      const mB = modal(env, api, inputOf(a)); await checkout(mB);
      targetUser = userB; targetPending = api.getPendingCheckout(userB, branchB);
      assert.ok(targetPending);
      find(a.render(), n => n.type === a.components.PosHeader).props.onLogout();
      assert.equal(a.state[0], null);
      await loginThroughForm(a, env, api, userB);
    }
    const paymentInput = inputOf(a);
    const pendingForUi = api.getPendingCheckout(a.state[0], a.state[3]);
    const ownPending = api.getUserPendingCheckout(targetUser);
    assert.ok(ownPending);
    assert.equal(ownPending.requestKey, targetPending.requestKey);
    assert.equal(paymentInput.items.length, 0, 'Counterexample: target owner cart was not recovered');
    const beforeAdd = paymentInput.items.length;
    addThroughSearch(a);
    const observed = {
      scenario:pathKind, authenticatedUserId:a.state[0].id,
      expectedBranch:targetPending.owner.branchId, actualBranch:a.state[3],
      expectedCartRows:targetPending.requestPayload.items.length, actualCartRows:beforeAdd,
      ownPendingRetained:true, pendingVisibleForSelectedBranch:Boolean(pendingForUi),
      addNewItemAllowed:a.state[1].length > beforeAdd,
    };
    if (pathKind === 'expiry-switch-back') {
      assert.notEqual(a.state[3], targetPending.owner.branchId);
      assert.equal(pendingForUi, null);
      assert.equal(observed.addNewItemAllowed, true);
      const m = modal(env, api, inputOf(a)); await checkout(m);
      observed.sentNewKeyInsteadOfRecovering = requests[requests.length-1].requestKey !== targetPending.requestKey;
      observed.newRequestBranch = requests[requests.length-1].branchId;
      assert.equal(observed.sentNewKeyInsteadOfRecovering, true);
    }
    observations.push(observed);
  }
  const result = {provider:'Actual LoginModal form, App callbacks, PaymentModal handlers and API production TS/TSX; Node VM hooks/storage/fetch stub; no browser/HTTP/PostgreSQL', observations};
  fs.writeFileSync(path.join(__dirname,'recovery-results.json'), JSON.stringify(result,null,2));
  console.log(JSON.stringify(result,null,2));
})().catch(err => {console.error(err);process.exitCode=1;});
`;
new Function('require', '__dirname', harness + scenarios)(require, __dirname);
