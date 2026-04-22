import re

with open('main.tex', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the start of Chapter 1
start_marker = r"% ============================================================\n%                  CHAPTER 1: INTRODUCTION"
end_marker = r"% ============================================================\n%                      REFERENCES"

start_idx = content.find("% ============================================================\n%                  CHAPTER 1: INTRODUCTION")
end_idx = content.find("% ============================================================\n%                      REFERENCES")

if start_idx != -1 and end_idx != -1:
    inputs = """% ============================================================
%                      MAIN CONTENT INCLUDES
% ============================================================
\\input{chapters/chap1}
\\input{chapters/chap2}
\\input{chapters/chap3}
\\input{chapters/chap4}
\\input{chapters/chap5}
\\input{chapters/chap6}
\\input{chapters/chap7}
\\input{chapters/chap8}
\\input{chapters/chap9}

"""
    with open('main.tex', 'w', encoding='utf-8') as f:
        f.write(content[:start_idx] + inputs + content[end_idx:])
    print("Successfully replaced monolithic content with inputs.")
else:
    print("Failed to find markers.")
