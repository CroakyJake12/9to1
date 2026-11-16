# Computer Science programming diagnostic — 10 challenges

OCR H446 practice, not an official OCR paper. Work **without an AI solution initially**. Use Python 3 or OCR-style pseudocode. Tutor: ask for one response at a time, do not reveal later solutions, and distinguish an assisted solution from independent success. Executable tests do not by themselves certify OCR marks.

## 1. Validation and loops — 5 marks, suggested 5 min
Write `valid_mark(mark)` returning `True` only for an **integer** mark from 0 to 100 inclusive. In Python, reject Booleans even though `bool` subclasses `int`. Show your result for -1, 0, 37, 100, 101 and `"37"`.

## 2. Lists and a single pass — 6 marks, suggested 6 min
Write `second_largest_distinct(values)` returning the second largest **distinct** integer in a nonempty list, or `None` if fewer than 2 distinct values exist. Do not sort the entire list or use `set`. Test `[5,2,5,4,4]`, `[7,7]`, `[-3,-1,-2]`.

## 3. Strings and algorithms — 5 marks, suggested 5 min
Write `is_palindrome(text)` that ignores case, spaces and punctuation; only letters and digits count. State an edge case. Test `"A man, a plan, a canal: Panama!"`, `"OpenCode"` and the empty string (decide on and justify empty-string behaviour).

## 4. Trace and state — 5 marks, suggested 5 min
Trace this pseudocode manually and give the final value of `total` and each intermediate `(i,total)`:
```
total = 0
FOR i = 1 TO 5
  IF i MOD 2 = 0 THEN
    total = total + i * 2
  ELSE
    total = total - 1
  ENDIF
ENDFOR
```
Explain how `MOD` affects the branch.

## 5. Search and complexity — 7 marks, suggested 7 min
Implement iterative binary search returning the index of `target` in a sorted ascending array (or -1). Explain what happens with duplicates and give best/worst-case time complexity. Show midpoint updates on `[2,4,6,8,10,12,14]`, target 12.

## 6. Sorting and reasoning — 7 marks, suggested 7 min
Write insertion sort for integers **without** using built-in sorting. Explain the loop invariant in plain English and how the algorithm performs on an already sorted list. Use `[4,1,3,1,2]`.

## 7. Dictionaries and data aggregation — 6 marks, suggested 6 min
Write `frequencies(words)` that lowercases words and returns a map from word to frequency. For `['Law','maths','law','CS','Maths','LAW']`, show the result. Give one way a dictionary differs from an array in access.

## 8. Recursion and base cases — 7 marks, suggested 7 min
Write a recursive `digit_sum(n)` for nonnegative integers. Show the call sequence for 407. Explain why the recursion terminates and why a zero input must work.

## 9. OOP and invariants — 8 marks, suggested 9 min
Design a `BankAccount` class with a private balance, methods to deposit/withdraw a nonnegative amount, rejection of overdrafts, and a read-only way to inspect the balance. Explain encapsulation and give at least three test cases. Use a fictional account; do not use real personal banking data.

## 10. Decomposition, testing and robustness — 10 marks, suggested 12 min
Design a small grade-processing system reading a list of `(student_id, mark)` pairs. Validate each record, calculate the arithmetic mean of **valid** marks, and return IDs of students who scored strictly above that mean. Describe functions, inputs/outputs, three boundary tests, and how invalid records are handled. Then implement (or give OCR-style pseudocode) and estimate complexity.

## Feedback framework
For each task record separately: (a) correct concept; (b) code/pseudocode correctness; (c) boundary-case handling; (d) explanation and complexity, where relevant; (e) independently demonstrated vs assisted. Award provisional task points only with stated assumptions; these are practice weights, not OCR official mark allocations.

## Priority after diagnostic
Group weaknesses into programming foundations, data structures, algorithms, OOP, testing and algorithmic reasoning. Teach the weakest prerequisite that blocks multiple questions first. Re-test with unseen questions of a similar shape.
