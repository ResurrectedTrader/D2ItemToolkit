/** C#'s FormatException: the one failure TryEvaluate reports as "unevaluable". */
class UsageFormatError extends Error {}

/**
 * D2R's items.txt `UsageConditionCalc`, evaluated for the name colour (ITEMS_GetName 0x14015926f:
 * a result of 0 reddens the name). The game compiles these with its general calc engine; this
 * covers the grammar the shipped cells use — integers, parentheses, `?:` and the two conditions the
 * Worldstone Shards test — and reports anything else as unevaluable rather than guessing.
 *
 * sub_1402790e0 evaluates the conditions: `cond('Difficulty', x)` is the game's difficulty equal
 * to x (normal 0, nightmare 1, hell 2, linked at 0x140278ea6), and
 * `cond('IsDesecratedZonesEnabled')` asks the game.
 */
export class UsageCondition {
  private at = 0;

  private constructor(
    private readonly text: string,
    private readonly difficulty: number,
    private readonly desecratedZones: boolean,
  ) {}

  /** The value, or null when the expression is outside the modelled grammar. */
  static tryEvaluate(
    expression: string | null,
    difficulty: number,
    desecratedZones: boolean,
  ): number | null {
    if (expression === null || expression.length === 0) {
      return null;
    }

    // The cells carry spreadsheet quoting, which the compiled .bin shows the calc compiler
    // accepts.
    if (expression.length >= 2 && expression.startsWith('"') && expression.endsWith('"')) {
      expression = expression.substring(1, expression.length - 1);
    }

    const parser = new UsageCondition(expression, difficulty, desecratedZones);
    try {
      const value = parser.expression();
      parser.skipSpace();
      return parser.at === parser.text.length ? value : null;
    } catch (error) {
      if (error instanceof UsageFormatError) {
        return null;
      }

      throw error;
    }
  }

  private expression(): number {
    const condition = this.primary();
    this.skipSpace();
    if (!this.accept('?')) {
      return condition;
    }

    const whenTrue = this.expression();
    this.expect(':');
    const whenFalse = this.expression();
    return condition !== 0 ? whenTrue : whenFalse;
  }

  private primary(): number {
    this.skipSpace();
    if (this.accept('(')) {
      const value = this.expression();
      this.expect(')');
      return value;
    }

    if (this.at < this.text.length && isDigit(this.text.charAt(this.at))) {
      const start = this.at;
      while (this.at < this.text.length && isDigit(this.text.charAt(this.at))) {
        ++this.at;
      }

      const value = Number(this.text.substring(start, this.at));
      if (value > 2147483647) {
        throw new UsageFormatError('Number out of range.');
      }

      return value;
    }

    const name = this.identifier();
    if (name.toLowerCase() !== 'cond') {
      throw new UsageFormatError('Unsupported calc function: ' + name);
    }

    this.expect('(');
    const condition = this.quoted();
    let argument: string | null = null;
    if (this.accept(',')) {
      this.skipSpace();
      argument = this.identifier();
    }

    this.expect(')');
    return this.condition(condition, argument);
  }

  private condition(name: string, argument: string | null): number {
    if (name.toLowerCase() === 'isdesecratedzonesenabled' && argument === null) {
      return this.desecratedZones ? 1 : 0;
    }

    if (name.toLowerCase() === 'difficulty' && argument !== null) {
      let wanted: number;
      switch (argument.toLowerCase()) {
        case 'normal':
          wanted = 0;
          break;
        case 'nightmare':
          wanted = 1;
          break;
        case 'hell':
          wanted = 2;
          break;
        default:
          throw new UsageFormatError('Unknown difficulty: ' + argument);
      }

      return this.difficulty === wanted ? 1 : 0;
    }

    throw new UsageFormatError('Unsupported condition: ' + name);
  }

  private identifier(): string {
    const start = this.at;
    while (
      this.at < this.text.length &&
      (/[\p{L}\p{Nd}]/u.test(this.text.charAt(this.at)) || this.text.charAt(this.at) === '_')
    ) {
      ++this.at;
    }

    if (this.at === start) {
      throw new UsageFormatError('Expected an identifier.');
    }

    return this.text.substring(start, this.at);
  }

  private quoted(): string {
    this.skipSpace();
    this.expect("'");
    const start = this.at;
    while (this.at < this.text.length && this.text.charAt(this.at) !== "'") {
      ++this.at;
    }

    const value = this.text.substring(start, this.at);
    this.expect("'");
    return value;
  }

  private skipSpace(): void {
    while (this.at < this.text.length && /\s/.test(this.text.charAt(this.at))) {
      ++this.at;
    }
  }

  private accept(c: string): boolean {
    this.skipSpace();
    if (this.at < this.text.length && this.text.charAt(this.at) === c) {
      ++this.at;
      return true;
    }

    return false;
  }

  private expect(c: string): void {
    if (!this.accept(c)) {
      throw new UsageFormatError("Expected '" + c + "'.");
    }
  }
}

function isDigit(c: string): boolean {
  return c >= '0' && c <= '9';
}
