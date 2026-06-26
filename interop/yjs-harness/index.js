// Copyright (c) marcschier. Licensed under the MIT License.
import fs from 'node:fs/promises';
import process from 'node:process';
import * as Y from 'yjs';

const selfTestScenario = {
  name: 'self-test',
  initial: 'ab',
  replicas: [
    {
      id: 'A',
      ops: [
        {
          op: 'insert',
          index: 1,
          text: 'X'
        }
      ]
    },
    {
      id: 'B',
      ops: [
        {
          op: 'insert',
          index: 1,
          text: 'Y'
        }
      ]
    }
  ],
  mergeSchedule: ['A<-B', 'B<-A']
};

async function readScenario() {
  const arg = process.argv[2];
  if (arg === '--self-test') {
    return selfTestScenario;
  }

  if (arg) {
    return JSON.parse(await fs.readFile(arg, 'utf8'));
  }

  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }

  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

function applyOp(text, op) {
  if (op.op === 'insert') {
    text.insert(op.index, op.text);
    return;
  }

  if (op.op === 'delete') {
    text.delete(op.index, op.length);
    return;
  }

  throw new Error(`Unsupported op '${op.op}'.`);
}

function parseMerge(step) {
  const parts = step.split('<-');
  if (parts.length !== 2 || !parts[0] || !parts[1]) {
    throw new Error(`Invalid merge step '${step}'. Expected 'A<-B'.`);
  }

  return {
    target: parts[0],
    source: parts[1]
  };
}

function createForkedDocs(scenario) {
  const baseDoc = new Y.Doc();
  baseDoc.getText('text').insert(0, scenario.initial ?? '');
  const baseUpdate = Y.encodeStateAsUpdate(baseDoc);
  const docs = new Map();

  for (const replica of scenario.replicas ?? []) {
    const doc = new Y.Doc();
    Y.applyUpdate(doc, baseUpdate);
    docs.set(replica.id, doc);
  }

  return docs;
}

function runScenario(scenario) {
  const docs = createForkedDocs(scenario);

  for (const replica of scenario.replicas ?? []) {
    const doc = docs.get(replica.id);
    if (!doc) {
      throw new Error(`Replica '${replica.id}' was not initialized.`);
    }

    const text = doc.getText('text');
    for (const op of replica.ops ?? []) {
      applyOp(text, op);
    }
  }

  for (const step of scenario.mergeSchedule ?? []) {
    const { target, source } = parseMerge(step);
    const sourceDoc = docs.get(source);
    const targetDoc = docs.get(target);
    if (!sourceDoc || !targetDoc) {
      throw new Error(`Merge step '${step}' references an unknown replica.`);
    }

    Y.applyUpdate(targetDoc, Y.encodeStateAsUpdate(sourceDoc));
  }

  const final = {};
  for (const [id, doc] of docs) {
    final[id] = doc.getText('text').toString();
  }

  return { final };
}

try {
  const scenario = await readScenario();
  const result = runScenario(scenario);
  console.log(JSON.stringify(result));
} catch (error) {
  console.error(error instanceof Error ? error.stack : String(error));
  process.exit(1);
}
