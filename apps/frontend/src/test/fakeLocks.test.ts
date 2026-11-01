import { describe, expect, it } from 'vitest';
import { FakeLockManager, deferred } from './fakeLocks';

describe('FakeLockManager', () => {
  it('dois pedidos sobrepostos pelo mesmo nome rodam em série', async () => {
    const locks = new FakeLockManager();
    const events: string[] = [];
    const gate = deferred<void>();

    const first = locks.request('x', async () => {
      events.push('a:start');
      await gate.promise;
      events.push('a:end');
    });
    const second = locks.request('x', () => {
      events.push('b:start');
    });

    await Promise.resolve();
    await Promise.resolve();
    expect(events).toEqual(['a:start']);

    gate.resolve();
    await Promise.all([first, second]);
    expect(events).toEqual(['a:start', 'a:end', 'b:start']);
  });

  it('um pedido que falha não prende o seguinte', async () => {
    const locks = new FakeLockManager();

    await expect(
      locks.request('x', () => {
        throw new Error('falhou');
      }),
    ).rejects.toThrow('falhou');

    await expect(locks.request('x', () => 'depois')).resolves.toBe('depois');
  });

  it('nomes diferentes não se esperam', async () => {
    const locks = new FakeLockManager();
    const gate = deferred<void>();
    let other = false;

    const held = locks.request('x', () => gate.promise);
    await locks.request('y', () => {
      other = true;
    });

    expect(other).toBe(true);
    gate.resolve();
    await held;
  });
});
