"""Small structural call graph for cache cleanup guards, not a C# semantic compiler.

Only real OnDestroy declarations are roots. Resolve same-type calls, static type
calls and declared instance receivers across partial files. Ignore comments,
literals, unused helpers/lambdas and plainly dead constant/return branches. Dynamic
dispatch, general boolean proofs and Unity callback registration still need L2/L3.
"""
import re

from cs_source_util import clean_source


TOKEN = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|'
                   r'[A-Za-z_]\w*|=>|\?\.|::|[^\s]')
IDENT = re.compile(r"^[A-Za-z_]\w*$")
MODIFIERS = {"public", "private", "protected", "internal", "static", "readonly",
             "override", "virtual", "sealed", "abstract", "partial", "async", "new"}
RESET = re.compile(r"^(?:Reset\w*StaticCaches|Clear\w*StaticCaches?|ForceCleanup)$")


def clean_structure_source(source):
    # Region descriptions can contain paths such as Foo/*.json, which are text,
    # not block comments. Preserve conditional directives for clean_source first.
    source = re.sub(r"^[ \t]*#(?!if\b|elif\b|else\b|endif\b)[^\n]*", "", source, flags=re.M)
    return clean_source(source)


def tokenize(source):
    source = re.sub(r"^[ \t]*#[^\n]*", "", clean_structure_source(source), flags=re.M)
    return ["<literal>" if token.startswith(('"', "'", '@"')) else token
            for token in TOKEN.findall(source)]


def pairs(tokens):
    result, stack = {}, []
    for index, token in enumerate(tokens):
        if token in ("(", "[", "{"):
            stack.append((token, index))
        elif token in (")", "]", "}"):
            if not stack or stack[-1][0] != {")": "(", "]": "[", "}": "{"}[token]:
                raise ValueError("unbalanced C# structure")
            _, opening = stack.pop()
            result[opening] = index
            result[index] = opening
    if stack:
        raise ValueError("unclosed C# structure")
    return result


def end_statement(tokens, links, start, end):
    index = start
    while index < end:
        if tokens[index] == ";":
            return index + 1
        if tokens[index] in ("(", "[", "{"):
            index = links[index] + 1
        else:
            index += 1
    return end


def arguments(tokens):
    if not tokens:
        return []
    result, start, depth = [], 0, 0
    for index, token in enumerate(tokens):
        if token in ("(", "[", "{", "<"):
            depth += 1
        elif token in (")", "]", "}", ">"):
            depth -= 1
        elif token == "," and depth == 0:
            result.append(tokens[start:index])
            start = index + 1
    return result + [tokens[start:]]


def live_tokens(tokens):
    """Return indices that are not in an obvious dead statement path."""
    links = pairs(tokens)

    def block(start, end):
        live, falls = set(), True
        while start < end and falls:
            start, falls, statement_live = statement(start, end)
            live.update(statement_live)
        return falls, live

    def statement(start, end):
        token = tokens[start]
        if token == "{":
            close = links[start]
            falls, live = block(start + 1, close)
            return close + 1, falls, live
        if token == "if" and start + 1 < end and tokens[start + 1] == "(":
            close = links[start + 1]
            condition = "".join(t for t in tokens[start + 2:close] if t not in ("(", ")"))
            truth = {"true": True, "!false": True, "false": False, "!true": False}.get(condition)
            after, yes_falls, yes_live = statement(close + 1, end)
            no_falls, no_live = True, set()
            if after < end and tokens[after] == "else":
                after, no_falls, no_live = statement(after + 1, end)
            live = set(range(start, close + 1))
            if truth is not False:
                live.update(yes_live)
            if truth is not True:
                live.update(no_live)
            falls = yes_falls if truth is True else no_falls if truth is False else yes_falls or no_falls
            return after, falls, live
        if token in ("while", "for", "foreach", "using", "lock", "switch") and tokens[start + 1] == "(":
            close = links[start + 1]
            after, _, body_live = statement(close + 1, end)
            condition = "".join(tokens[start + 2:close])
            live = set(range(start, close + 1))
            if not (token == "while" and condition == "false"):
                # Switch labels have separate entry paths; don't treat the first
                # case's return as making every later case unreachable.
                if token == "switch":
                    body_live = set(range(close + 1, after))
                live.update(body_live)
            return after, True, live
        if token == "try":
            after, falls, live = statement(start + 1, end)
            while after < end and tokens[after] in ("catch", "finally"):
                kind = tokens[after]
                after += 1
                if after < end and tokens[after] == "(":
                    after = links[after] + 1
                if after < end and tokens[after] == "when":
                    after = links[after + 1] + 1
                after, branch_falls, branch_live = statement(after, end)
                live.update(branch_live)
                falls = falls and branch_falls if kind == "finally" else falls or branch_falls
            return after, falls, live
        # A local function declaration creates no execution edge. Fail closed on
        # local-function indirection rather than crediting its unused body.
        if start + 3 < end and IDENT.fullmatch(token) and IDENT.fullmatch(tokens[start + 1]) and tokens[start + 2] == "(":
            close = links[start + 2]
            if close + 1 < end and tokens[close + 1] == "{":
                return links[close + 1] + 1, True, set()
        after = end_statement(tokens, links, start, end)
        return after, token not in ("return", "throw"), set(range(start, after))

    return block(0, len(tokens))[1]


def read_members(all_files):
    methods, receivers, bases, destroy_roots = {}, {}, {}, set()
    for path, source in all_files.items():
        tokens = tokenize(source)
        try:
            links = pairs(tokens)
        except ValueError as error:
            raise ValueError(str(path) + ": " + str(error)) from error
        for pos, token in enumerate(tokens[:-1]):
            if token not in ("class", "struct") or not IDENT.fullmatch(tokens[pos + 1]):
                continue
            owner = tokens[pos + 1]
            opening = pos + 2
            while opening < len(tokens) and tokens[opening] not in ("{", ";"):
                opening += 1
            if opening == len(tokens) or tokens[opening] != "{":
                continue
            if ":" in tokens[pos + 2:opening]:
                colon = tokens.index(":", pos + 2, opening)
                bases[owner] = tokens[colon + 1]
            index, end = opening + 1, links[opening]
            while index < end:
                if tokens[index] == "[":
                    index = links[index] + 1
                    continue
                start, parameter = index, None
                while index < end and tokens[index] not in ("{", "=>", ";"):
                    if tokens[index] == "(":
                        if parameter is None and "=" not in tokens[start:index]:
                            parameter = index
                        index = links[index] + 1
                    elif tokens[index] == "[":
                        index = links[index] + 1
                    else:
                        index += 1
                if index == end:
                    break
                header = tokens[start:index]
                body_end = links[index] + 1 if tokens[index] == "{" else end_statement(tokens, links, index, end)
                if parameter is not None and tokens[index] in ("{", "=>") and not ({"class", "struct", "delegate"} & set(header)):
                    name = tokens[parameter - 1]
                    if IDENT.fullmatch(name):
                        body = tokens[index + 1:body_end - 1]
                        parameters = arguments(tokens[parameter + 1:links[parameter]])
                        # Unity's message has zero declared parameters. Optional
                        # parameters support helper invocation, not callback roots.
                        if name == "OnDestroy" and not parameters and tokens[parameter - 2] == "void" and "static" not in header:
                            destroy_roots.add((owner, name, 0))
                        minimum = sum("=" not in arg for arg in parameters)
                        for count in range(minimum, len(parameters) + 1):
                            methods.setdefault((owner, name, count), []).append(body)
                elif not ({"class", "struct", "enum", "event", "delegate"} & set(header)):
                    declaration = header[:header.index("=")] if "=" in header else header
                    declaration = [t for t in declaration if t not in MODIFIERS]
                    if len(declaration) >= 2 and IDENT.fullmatch(declaration[-1]):
                        # Cleanup receivers are typed fields/properties. The last
                        # identifier before their name also handles namespace paths.
                        type_names = [t for t in declaration[:-1] if IDENT.fullmatch(t)]
                        if type_names:
                            receivers[(owner, declaration[-1])] = type_names[-1]
                index = body_end
    return methods, receivers, bases, destroy_roots


def reachable_reset_methods(all_files):
    methods, receivers, bases, destroy_roots = read_members(all_files)
    types = {owner for owner, _, _ in methods}

    def member_type(owner, member):
        visited = set()
        while owner and owner not in visited:
            visited.add(owner)
            if (owner, member) in receivers:
                return receivers[(owner, member)]
            owner = bases.get(owner)
        return None

    def invocation(tokens, pos):
        chain = [tokens[pos]]
        while pos >= 2 and tokens[pos - 1] in (".", "?.", "::") and IDENT.fullmatch(tokens[pos - 2]):
            chain.insert(0, tokens[pos - 2])
            pos -= 2
        return chain

    def calls(owner, tokens):
        links, live = pairs(tokens), live_tokens(tokens)
        lambdas = []
        for index, token in enumerate(tokens):
            if token != "=>":
                continue
            close = links[index + 1] if tokens[index + 1] == "{" else index + 1
            if tokens[index + 1] != "{":
                while close < len(tokens) and tokens[close] not in (";", ",", ")"):
                    close = links[close] + 1 if tokens[close] in ("(", "[") else close + 1
            # SafeRuntime.Run invokes its Action synchronously. Merely constructing
            # a delegate elsewhere is not an edge in the destroy call graph.
            synchronous = any(tokens[p] == "(" and p < index < links[p]
                              and p > 0 and invocation(tokens, p - 1) == ["SafeRuntime", "Run"]
                              for p in links if p < links[p])
            if not synchronous:
                lambdas.append((index, close))
        for index, token in enumerate(tokens[:-1]):
            if index not in live or tokens[index + 1] != "(" or not IDENT.fullmatch(token):
                continue
            if any(start < index <= end for start, end in lambdas):
                continue
            chain = invocation(tokens, index)
            called_owner = owner
            if len(chain) > 1:
                qualifier = chain[:-1]
                if qualifier[0] in ("this", "base"):
                    if qualifier[0] == "base":
                        called_owner = bases.get(owner)
                    qualifier = qualifier[1:]
                if qualifier:
                    if qualifier[-1] in types:
                        called_owner = qualifier[-1]
                    else:
                        for receiver in qualifier:
                            called_owner = member_type(called_owner, receiver)
                            if called_owner is None:
                                break
            count = len(arguments(tokens[index + 2:links[index + 1]]))
            key = (called_owner, chain[-1], count)
            if key in methods:
                yield key

    pending = list(destroy_roots)
    reached = set()
    while pending:
        key = pending.pop()
        if key in reached:
            continue
        reached.add(key)
        for body in methods[key]:
            pending.extend(calls(key[0], body))
    return {(owner, name) for owner, name, _ in reached if RESET.fullmatch(name)}
