// Model checks only. These samples are synthetic and are not hardware results.
import assert from 'node:assert/strict';

let seed = 0x6a09e667;
function random() {
  seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
  return seed / 4294967296;
}

function predicate(lower, upper, threshold) {
  return lower > threshold ? 1 : upper <= threshold ? 0 : -1;
}

function conjunction(left, right) {
  return left === 0 || right === 0 ? 0 : left === 1 && right === 1 ? 1 : -1;
}

function disjunction(left, right) {
  return left === 1 || right === 1 ? 1 : left === 0 && right === 0 ? 0 : -1;
}

function negation(value) {
  return value < 0 ? -1 : 1 - value;
}

function record(summary, decision, truth) {
  if (decision < 0) {
    summary.unknown++;
  } else {
    summary.decided++;
    if (decision !== truth) summary.wrong++;
  }
}

const count = 100000;
const result = {
  description: 'Synthetic bounded affine signals; illustrative distribution only',
  seed: '0x6a09e667',
  cases: count,
  enclosureViolations: 0,
  latest: { decided: 0, wrong: 0, unknown: 0 },
  fixedAge100ms: { decided: 0, wrong: 0, unknown: 0 },
  robustAnd: { decided: 0, wrong: 0, unknown: 0 },
  robustOrNot: { decided: 0, wrong: 0, unknown: 0 },
  persistenceChecks: 0,
  persistenceViolations: 0,
};

for (let index = 0; index < count; index++) {
  const samples = [];
  for (let channel = 0; channel < 2; channel++) {
    const threshold = channel === 0 ? 3 : 40;
    const maxRate = channel === 0 ? 2 : 20;
    const error = channel === 0 ? 0.02 : 0.1;
    const minimumAge = random() * (channel === 0 ? 0.04 : 0.5);
    const timeUncertainty = channel === 0 ? 0.001 : random() * 0.05;
    const actualAge = minimumAge + timeUncertainty * random();
    const measured = threshold + (random() * 2 - 1) * (channel === 0 ? 0.6 : 6);
    const noise = (random() * 2 - 1) * error;
    const slope = (random() * 2 - 1) * maxRate;
    const actual = measured - noise + slope * actualAge;
    const maximumAge = minimumAge + timeUncertainty;
    const radius = error + maxRate * maximumAge;
    const lower = measured - radius;
    const upper = measured + radius;
    if (actual < lower - 1e-12 || actual > upper + 1e-12) {
      result.enclosureViolations++;
    }
    samples.push({ threshold, measured, actual, maximumAge, lower, upper,
      slope, maxRate, error });
  }

  const truths = samples.map(sample => Number(sample.actual > sample.threshold));
  const decisions = samples.map(sample => predicate(sample.lower, sample.upper, sample.threshold));
  const truth = truths[0] && truths[1] ? 1 : 0;
  const latest = samples.every(sample => sample.measured > sample.threshold) ? 1 : 0;
  record(result.latest, latest, truth);
  record(result.fixedAge100ms,
    samples.every(sample => sample.maximumAge <= 0.1) ? latest : -1, truth);
  record(result.robustAnd, conjunction(decisions[0], decisions[1]), truth);
  record(result.robustOrNot,
    disjunction(decisions[0], negation(decisions[1])),
    truths[0] || !truths[1] ? 1 : 0);

  const decision = conjunction(decisions[0], decisions[1]);
  if (decision >= 0) {
    const horizons = samples.map(sample =>
      (Math.abs(sample.measured - sample.threshold) - sample.error) / sample.maxRate
        - sample.maximumAge);
    const horizon = decision === 1
      ? Math.min(...horizons)
      : Math.max(...horizons.filter((_, channel) => decisions[channel] === 0));
    if (horizon > 0) {
      // Deterministic interior point, preserving the random stream above.
      const after = horizon * 0.731;
      const futureTruth = samples.every(sample =>
        sample.actual + sample.slope * after > sample.threshold) ? 1 : 0;
      result.persistenceChecks++;
      if (futureTruth !== decision) result.persistenceViolations++;
    }
  }
}

assert.equal(result.enclosureViolations, 0);
assert.equal(result.robustAnd.wrong, 0);
assert.equal(result.robustOrNot.wrong, 0);
assert.equal(result.persistenceViolations, 0);
assert.ok(result.persistenceChecks > 0);
assert.ok(result.robustAnd.decided > 0 && result.robustAnd.unknown > 0);
assert.equal(predicate(3, 3.1, 3), -1);
assert.equal(predicate(2.9, 3, 3), 0);
assert.equal(negation(-1), -1);

const value = 5;
const error = 0.02;
const maxRate = 1;
result.ruleBudgetsSeconds = {
  threshold3V: (Math.abs(value - 3) - error) / maxRate,
  threshold4_95V: (Math.abs(value - 4.95) - error) / maxRate,
};

// The guarantee is conditional: an unmodelled jump defeats the assumed bound.
const predicted = predicate(5 - 0.1 * 0.1, 5 + 0.1 * 0.1, 3);
const actualAfterJump = Number(2 > 3);
assert.notEqual(predicted, actualAfterJump);
result.assumptionViolationExample = {
  assumedMaxRate: 0.1,
  ageSeconds: 0.1,
  sampledValue: 5,
  actualAfterUnmodelledJump: 2,
  predictionIsWrong: true,
  explanation: 'An invalid rate bound invalidates the guarantee',
};

console.log(JSON.stringify(result, null, 2));
