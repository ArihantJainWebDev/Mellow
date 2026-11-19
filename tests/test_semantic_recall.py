import unittest
from unittest.mock import patch

from mellow.providers.embeddings import EmbeddingError
from mellow.retrieval import SemanticRecall


class SemanticRecallTests(unittest.TestCase):
    def test_paraphrase_edit_and_removal(self):
        recall = SemanticRecall("test")
        preference = (1, "fact", "I prefer long answers", 1)
        unrelated = (2, "fact", "I own a red bicycle", 2)
        edited = (1, "fact", "I prefer short answers", 1)
        with patch("mellow.retrieval.embed", side_effect=[
            [(1, 0), (0, 1), (1, 0)],
            [(1, 0), (1, 0)],
            [(1, 0)],
        ]) as embed:
            query = "Do I like detailed explanations?"
            self.assertIn(preference[2], recall.find(query, [preference, unrelated])["content"])
            self.assertIn(edited[2], recall.find(query, [edited, unrelated])["content"])
            self.assertEqual(embed.call_args.args[1], [
                f"search_query: {query}", "search_document: I prefer short answers",
            ])
            self.assertIsNone(recall.find(query, [unrelated]))

    def test_unavailable_model_falls_back_without_repeated_requests(self):
        recall = SemanticRecall("test")
        memories = [(1, "fact", "I prefer long answers", 1)]
        with patch("mellow.retrieval.embed", side_effect=EmbeddingError) as embed:
            for _ in range(2):
                self.assertIn("long answers", recall.find("Do I prefer long answers?", memories)["content"])
            embed.assert_called_once()

    def test_semantic_nonmatch_does_not_override_threshold_with_keywords(self):
        recall = SemanticRecall("test")
        with patch("mellow.retrieval.embed", return_value=[(0, 1), (1, 0)]):
            self.assertIsNone(recall.find("What is a bicycle?", [(1, "fact", "I own a bicycle", 1)]))
