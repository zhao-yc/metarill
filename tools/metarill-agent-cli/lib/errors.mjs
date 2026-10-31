/** CLI 错误只携带稳定标识，不回显服务端原文或可能含令牌的请求。 */
export class CliError extends Error {
  constructor(message, code = 'cli_error', details = {}) {
    super(message);
    this.code = code;
    this.details = details;
  }
}
