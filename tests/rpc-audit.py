"""Read-only audit of every configured RPC; no keys, signing or broadcasts."""
import concurrent.futures
import json
import pathlib
import re
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
source = (ROOT / 'Web3/Rpc.cs').read_text(encoding='utf-8-sig')
ids = {name: int(value) for name, value in re.findall(r'(\w+)\s*=\s*(\d+)', source)}
urls = re.findall(r'\{RpcUrl\.(\w+),\s*"([^"]+)"\}', source)
if '--candidates' in sys.argv:
    urls = [('Fantom', url) for url in ['https://rpc.ftm.tools', 'https://fantom.drpc.org', 'https://fantom.publicnode.com']]
    urls += [('Gravity', url) for url in ['https://gravity-alpha.drpc.org', 'https://rpc.ankr.com/gravity', 'https://rpc.gravity.xyz/']]
    urls += [('Zero', url) for url in ['https://zerion.drpc.org', 'https://rpc.zero.network']]
WALLET = '0x0000000000000000000000000000000000000001'

def request(url, method=None, params=None):
    payload = None if method is None else json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': method, 'params': params or []}).encode()
    req = urllib.request.Request(url, data=payload, headers={'Content-Type': 'application/json', 'User-Agent': 'z3nBank-RPC-audit'})
    with urllib.request.urlopen(req, timeout=12) as response:
        body = json.load(response)
    if method is None:
        return body
    if body.get('error'):
        raise RuntimeError(json.dumps(body['error']))
    if 'result' not in body:
        raise RuntimeError('Missing JSON-RPC result')
    return body['result']

try:
    metadata = request('https://li.quest/v1/chains?chainTypes=EVM')['chains']
except Exception as error:
    metadata = []
    print('LI.FI metadata error:', str(error), flush=True)
by_id = {chain['id']: chain for chain in metadata}
try:
    tokens_by_id = request('https://li.quest/v1/tokens')['tokens']
except Exception as error:
    tokens_by_id = {}
    print('LI.FI token metadata error:', str(error), flush=True)

def probe(entry):
    name, url = entry
    report = {'network': name, 'expectedId': ids[name], 'url': url, 'checks': {}}
    report['expectedUnavailable'] = name == 'Zero'
    try:
        if name.startswith('Solana'):
            report['checks']['getHealth'] = request(url, 'getHealth')
        elif name == 'Aptos':
            report['checks']['ledger'] = request(url)['chain_id']
        else:
            chain_id = int(request(url, 'eth_chainId'), 16)
            report['actualId'] = chain_id
            if chain_id != ids[name]:
                raise RuntimeError(f'Wrong chain ID: configured {ids[name]}, actual {chain_id}')
            head = request(url, 'eth_blockNumber')
            report['checks']['eth_blockNumber'] = head
            amount = request(url, 'eth_getBalance', [WALLET, head])
            int(amount, 16)
            report['checks']['eth_getBalance'] = 'OK at explicit block'
            token = by_id.get(chain_id, {}).get('wrappedToken', {}).get('address')
            if not token:
                token = next((t['address'] for t in tokens_by_id.get(str(chain_id), [])
                              if t.get('address') and int(t['address'], 16) not in (0, int('e' * 40, 16))), None)
            if not token:
                token = {250: '0x21be370d5312f44cb42ce377bc9b8a0cef1a4c83',
                         169: '0x0dc808adce2099a9f62aa87d9670745aba741746',
                         167000: '0xa51894664a773981c6c112c43ce576f315d5b1b6',
                         7777777: '0x4200000000000000000000000000000000000006'}.get(chain_id)
            if token and int(token, 16):
                report['token'] = token
                value = request(url, 'eth_call', [{'to': token, 'data': '0x70a08231' + WALLET[2:].lower().zfill(64)}, head])
                if len(value) != 66:
                    raise RuntimeError(f'Invalid ERC20 balanceOf response: {value}')
                report['checks']['eth_call'] = 'OK: ERC20 balanceOf'
            else:
                value = request(url, 'eth_call', [{'to': '0x0000000000000000000000000000000000000000', 'data': '0x'}, head])
                report['checks']['eth_call'] = 'OK: call supported; no deployed ERC20 fixture for this testnet'
        report['ok'] = True
    except Exception as error:
        report['ok'] = False
        report['error'] = str(error)
    print(json.dumps(report), flush=True)
    return report

with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    results = list(pool.map(probe, urls))
output = ROOT / 'bin/TokenValidation/rpc-audit.json'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({'results': results, 'metadata': metadata}, indent=2), encoding='utf-8')
print(f"SUMMARY: {sum(r['ok'] for r in results)}/{len(results)} RPCs passed; report: {output}")
sys.exit(1 if any(not r['ok'] and not r['expectedUnavailable'] for r in results) else 0)
