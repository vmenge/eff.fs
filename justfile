set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

test *args:
    set -- {{args}}; \
    package_filter=""; \
    test_case_filter=""; \
    watch_mode="false"; \
    while [ "$#" -gt 0 ]; do \
      case "$1" in \
        -w) \
          watch_mode="true"; \
          shift; \
          ;; \
        -p) \
          if [ "$#" -lt 2 ]; then \
            echo "Missing package name after -p"; \
            exit 1; \
          fi; \
          package_filter="$2"; \
          shift 2; \
          ;; \
        *) \
          if [ -z "$test_case_filter" ]; then \
            test_case_filter="$1"; \
          else \
            test_case_filter="$test_case_filter $1"; \
          fi; \
          shift; \
          ;; \
      esac; \
    done; \
    projects=$(find . -path '*/tests/*.fsproj' ! -path '*/tests/Fixtures/*' | sort); \
    if [ -n "$package_filter" ]; then \
      projects=$(printf '%s\n' "$projects" | grep -Fi "$package_filter" || true); \
    fi; \
    if [ -z "$projects" ]; then \
      if [ -n "$package_filter" ]; then \
        echo "No test projects matched package filter: $package_filter"; \
      else \
        echo "No test projects found under */tests/"; \
      fi; \
      exit 1; \
    fi; \
    if [ "$watch_mode" = "true" ]; then \
      project_count=$(printf '%s\n' "$projects" | sed '/^$/d' | wc -l | tr -d ' '); \
      if [ "$project_count" -ne 1 ]; then \
        echo "Watch mode requires exactly one matching test project."; \
        echo "Matched projects:"; \
        printf '  %s\n' $projects; \
        exit 1; \
      fi; \
      project=$(printf '%s\n' "$projects" | head -n 1); \
      echo "==> dotnet watch --project $project run"; \
      if [ -n "$test_case_filter" ]; then \
        dotnet watch --project "$project" run -- --filter-test-case "$test_case_filter"; \
      else \
        dotnet watch --project "$project" run; \
      fi; \
      exit 0; \
    fi; \
    while IFS= read -r project; do \
      echo "==> dotnet run --project $project"; \
      if [ -n "$test_case_filter" ]; then \
        dotnet run --project "$project" -- --filter-test-case "$test_case_filter"; \
      else \
        dotnet run --project "$project"; \
      fi; \
    done <<< "$projects"
