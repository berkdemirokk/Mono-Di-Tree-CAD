import { sign } from 'node:crypto';

const { ASC_KEY_ID, ASC_ISSUER_ID, ASC_PRIVATE_KEY_BASE64, MARKETING_VERSION, BUILD_NUMBER } = process.env;
const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
const now = Math.floor(Date.now() / 1000);
const header = encode({ alg: 'ES256', kid: ASC_KEY_ID, typ: 'JWT' });
const claims = encode({ iss: ASC_ISSUER_ID, iat: now, exp: now + 180, aud: 'appstoreconnect-v1' });
const message = header + '.' + claims;
const signature = sign('sha256', Buffer.from(message), {
  key: Buffer.from(ASC_PRIVATE_KEY_BASE64, 'base64'),
  dsaEncoding: 'ieee-p1363',
}).toString('base64url');

async function read(path, params) {
  const url = new URL('/v1/' + path, 'https://api.appstoreconnect.apple.com');
  url.search = new URLSearchParams(params);
  const response = await fetch(url, {
    redirect: 'error',
    headers: { Authorization: 'Bearer ' + message + '.' + signature },
  });
  if (!response.ok) {
    const errors = await response.json();
    throw new Error('App Store Connect HTTP ' + response.status + ': ' +
      (errors.errors ?? []).map(error => error.code + ' ' + error.detail).join(', '));
  }
  return response.json();
}

function only(list, kind) {
  if (list.length !== 1) throw new Error('Expected one ' + kind + ', found ' + list.length);
  return list[0];
}

const apps = await read('apps', { 'filter[bundleId]': 'com.berkdemirokk.monoditreecad' });
const app = only(apps.data, 'app');
const versions = await read('preReleaseVersions', {
  'filter[app]': app.id,
  'filter[platform]': 'IOS',
  'filter[version]': MARKETING_VERSION,
});
const version = only(versions.data, 'iOS prerelease version');
const builds = await read('builds', {
  'filter[preReleaseVersion]': version.id,
  'filter[version]': BUILD_NUMBER,
  include: 'betaGroups,buildBetaDetail',
  'limit[betaGroups]': '50',
});
const build = only(builds.data, 'matching build');
const included = builds.included ?? [];
const groups = included.filter(resource => resource.type === 'betaGroups');
const groupCount = build.relationships?.betaGroups?.meta?.paging?.total;
if (groupCount !== undefined && groups.length !== groupCount) throw new Error('Incomplete beta group list');
const detail = only(included.filter(resource => resource.type === 'buildBetaDetails'), 'build beta detail');
console.log(JSON.stringify({
  version: MARKETING_VERSION, build: build.attributes.version,
  processing: build.attributes.processingState, expired: build.attributes.expired,
  internalState: detail.attributes.internalBuildState, externalState: detail.attributes.externalBuildState,
  internalGroups: groups.filter(group => group.attributes.isInternalGroup).length,
  externalGroups: groups.filter(group => !group.attributes.isInternalGroup).length,
}));
