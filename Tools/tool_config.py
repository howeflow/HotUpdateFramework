def load_config(path):
    if not path.exists():
        return {}

    config = {}
    with path.open("r", encoding="utf-8-sig") as file:
        for line_number, raw_line in enumerate(file, start=1):
            line = raw_line.strip()
            if not line or line.startswith("#") or line.startswith(";"):
                continue

            if line.startswith("export "):
                line = line[len("export "):].strip()

            if "=" not in line:
                raise ValueError(f"Invalid config line {line_number} in {path}: {raw_line.rstrip()}")

            key, value = line.split("=", 1)
            key = key.strip()
            if not key:
                raise ValueError(f"Empty config key at line {line_number} in {path}")

            config[key] = _strip_value(value)

    return config


def config_value(config, name, cli_value=None, default=None):
    if cli_value is not None:
        if not isinstance(cli_value, str) or cli_value.strip():
            return cli_value

    value = config.get(name)
    if value is not None:
        return value

    return default


def config_bool(config, name, default=False):
    value = config.get(name)
    if value is None:
        return default

    if isinstance(value, bool):
        return value

    text = str(value).strip().lower()
    if text in {"1", "true", "yes", "y", "on"}:
        return True
    if text in {"0", "false", "no", "n", "off", ""}:
        return False

    raise ValueError(f"Invalid boolean config value for {name}: {value}")


def _strip_value(value):
    text = _strip_inline_comment(value).strip()
    if len(text) >= 2 and text[0] == text[-1] and text[0] in {"'", '"'}:
        text = text[1:-1]
    return text


def _strip_inline_comment(value):
    in_quote = None
    escaped = False
    for index, char in enumerate(value):
        if escaped:
            escaped = False
            continue

        if char == "\\":
            escaped = True
            continue

        if char in {"'", '"'}:
            if in_quote == char:
                in_quote = None
            elif in_quote is None:
                in_quote = char
            continue

        if char in {"#", ";"} and in_quote is None:
            if index == 0 or value[index - 1].isspace():
                return value[:index]

    return value
