"""从原YZP列表生成器提取原始模板，只解释字符串拼接和固定布尔分支，不执行Java。"""
import argparse
import hashlib
import json
from pathlib import Path
import re


def statement(source, start):
    """只在字符串和注释之外识别分号，保留原始HTML/JS文本。"""
    index = start
    quoted = False
    while index < len(source):
        char = source[index]
        if quoted:
            if char == "\\":
                index += 2
                continue
            if char == '"':
                quoted = False
        elif source.startswith("//", index):
            index = source.index("\n", index)
            continue
        elif source.startswith("/*", index):
            index = source.index("*/", index + 2) + 2
            continue
        elif char == '"':
            quoted = True
        elif char == ";":
            return source[start:index], index + 1
        index += 1
    raise ValueError("原生成器字符串表达式未结束")


class Expression:
    """固定语法解释器拒绝调用、字段访问和未知符号，避免模板提取成为代码执行。"""
    token = re.compile(r'\s+|//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"|[A-Za-z_][A-Za-z0-9_]*|[()+?:]', re.S)

    def __init__(self, expression, values):
        self.tokens = []
        offset = 0
        while offset < len(expression):
            match = self.token.match(expression, offset)
            if not match:
                raise ValueError("未支持的Java表达式字符：" + expression[offset:offset + 40])
            token = match.group()
            if not token.isspace() and not token.startswith(("//", "/*")):
                self.tokens.append(token)
            offset = match.end()
        self.position = 0
        self.values = values

    def take(self, value):
        if self.position < len(self.tokens) and self.tokens[self.position] == value:
            self.position += 1
            return True
        return False

    def value(self):
        if self.take("("):
            result = self.conditional()
            if not self.take(")"):
                raise ValueError("Java表达式括号不匹配")
            return result
        token = self.tokens[self.position]
        self.position += 1
        if token.startswith('"'):
            return json.loads(token.replace("\\'", "'"))
        if token not in self.values:
            raise ValueError("未声明的生成变量：" + token)
        return self.values[token]

    def additive(self):
        result = self.value()
        while self.take("+"):
            following = self.value()
            if not isinstance(result, str) or not isinstance(following, str):
                raise ValueError("模板仅允许字符串拼接")
            result += following
        return result

    def conditional(self):
        condition = self.additive()
        if self.take("?"):
            left = self.conditional()
            if not self.take(":"):
                raise ValueError("条件表达式缺少冒号")
            right = self.conditional()
            if not isinstance(condition, bool):
                raise ValueError("条件必须是明确布尔值")
            return left if condition else right
        return condition

    def render(self):
        result = self.conditional()
        if self.position != len(self.tokens) or not isinstance(result, str):
            raise ValueError("未完整解析原表达式")
        return result


def extract(path):
    """分别保留普通列表和选择页脚本，以及三个移动端原布局分支。"""
    raw = path.read_bytes()
    source = raw.decode("utf-8-sig").replace("\r\n", "\n")
    source = source[source.index("private void GenerateList("):]
    start = source.index('String result = "<template>') + len("String result = ")
    prefix, end = statement(source, start)
    mobile = []
    for _ in range(3):
        start = source.index("result += ", end) + len("result += ")
        expression, end = statement(source, start)
        mobile.append(expression)
    start = source.index("result += ", end) + len("result += ")
    suffix, _ = statement(source, start)
    values = {name: "##" + name + "##" for name in ("listNewButtonVisibleCondition", "toDetailNewGuard", "toDetailQueryGuard")}
    result = {}
    for ordinary in (True, False):
        inputs = dict(values, useSharedListRuntime=ordinary)
        result["original-list-" + ("ordinary" if ordinary else "selection") + ".vue.tpl"] = Expression(prefix, inputs).render() + "##mobile_list##\n" + Expression(suffix, inputs).render()
    for index, expression in enumerate(mobile, 1):
        result[f"original-list-mobile-{index}.vue.tpl"] = Expression(expression, values).render()
    return result, {"sourceFile": "backend/yzp/src/main/java/com/yinzon/service/yzp/v8/UniappService.java", "sourceSha256": hashlib.sha256(raw).hexdigest(), "normalization": "UTF8无BOM及LF，Java字符串按原转义展开；DOM和脚本保留原文本", "templates": {key: hashlib.sha256(value.encode()).hexdigest() for key, value in result.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    templates, receipt = extract(args.source)
    templates["original-list-source.json"] = json.dumps(receipt, ensure_ascii=False, indent=2) + "\n"
    if not args.check:
        args.output.mkdir(parents=True, exist_ok=True)
    for name, content in templates.items():
        path = args.output / name
        if args.check:
            if not path.is_file() or path.read_bytes() != content.encode():
                raise ValueError("原模板提取产物不一致：" + name)
        else:
            path.write_bytes(content.encode())
    print("原列表模板逐分支提取核对通过：" + str(len(templates) - 1) + "份；未执行Java、数据库或服务。")


if __name__ == "__main__":
    main()
