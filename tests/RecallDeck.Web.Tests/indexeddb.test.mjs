import "fake-indexeddb/auto";
import { test } from "node:test";
import { cases } from "./storage-cases.mjs";

for (const [name, check] of cases) test(name, check);
