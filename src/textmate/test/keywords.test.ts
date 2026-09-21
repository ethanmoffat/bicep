// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { readFile } from 'fs/promises';
import { IOnigLib, parseRawGrammar, Registry } from 'vscode-textmate';
import { createOnigScanner, createOnigString, loadWASM } from 'vscode-oniguruma';
import { grammarPath } from '../src/bicep.js';

async function createOnigLib(): Promise<IOnigLib> {
  const onigWasm = await readFile(new URL('./onig.wasm', import.meta.resolve('vscode-oniguruma')));

  await loadWASM(onigWasm.buffer);

  return {
    createOnigScanner: sources => createOnigScanner(sources),
    createOnigString,
  };
}

const registry = new Registry({
  onigLib: createOnigLib(),
  loadGrammar: async () => parseRawGrammar(await readFile(grammarPath, { encoding: 'utf-8' })),
});

async function scopesOf(line: string, word: string): Promise<string[]> {
  const grammar = await registry.loadGrammar('source.bicep');
  if (!grammar) {
    throw new Error('Unable to load the Bicep grammar.');
  }

  const token = grammar
    .tokenizeLine(line, null)
    .tokens.find(t => line.substring(t.startIndex, t.endIndex) === word);

  if (!token) {
    throw new Error(`No token exactly matching '${word}' in '${line}'.`);
  }

  return token.scopes.filter(s => s !== 'source.bicep');
}

async function isKeyword(line: string, word: string): Promise<boolean> {
  return (await scopesOf(line, word)).some(s => s.startsWith('keyword'));
}

describe('declaration keywords', () => {
  // `test` and `case` introduce declarations, but unlike `resource` or `param` they are not
  // reserved, so they may legally name a variable, parameter or module. Highlighting them wherever
  // the word appears would recolor working `.bicep` files.
  it.each([
    ['test moduleSourcePolicy = {', 'test'],
    ['case shortPrefix = {', 'case'],
  ])('highlights %p where a declaration starts', async (line, word) => {
    expect(await isKeyword(line, word)).toBe(true);
  });

  it.each([
    ['var test = 1', 'test'],
    ['var case = 1', 'case'],
    ["param test string = 'x'", 'test'],
    ["module test './x.bicep' = {", 'test'],
    ['output o int = test', 'test'],
  ])('leaves %p alone where the word is a name', async (line, word) => {
    expect(await isKeyword(line, word)).toBe(false);
  });
});

describe('keywords after a dot', () => {
  // `\b` treats a dot as a word boundary, so without an explicit guard a property whose name
  // happens to match a keyword is highlighted as one.
  it.each([
    ['failOn: filter(facts, r => r.existing)', 'existing'],
    ['var x = foo.metadata', 'metadata'],
  ])('treats %p as a property rather than a keyword', async (line, word) => {
    expect(await isKeyword(line, word)).toBe(false);
  });

  it('still highlights the keyword where it is one', async () => {
    expect(await isKeyword("resource a 'Microsoft.Foo/bar@2020-01-01' existing = {", 'existing')).toBe(true);
  });
});
