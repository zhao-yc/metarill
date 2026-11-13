#!/usr/bin/env node
import { main } from "../lib/cli.mjs";

// 统一入口保持标准输出为机器可读 JSON，错误写入标准错误。
process.exitCode = await main(process.argv.slice(2));
